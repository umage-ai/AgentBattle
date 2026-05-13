using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Mcp;
using AgentBattle.Orchestrator.Recording;
using AgentBattle.Orchestrator.TurnLoop;

namespace AgentBattle.Orchestrator;

/// <summary>
/// Top-level coordinator for a full poker match. Builds per-agent sessions, loops hands and turns,
/// and emits a complete battle event stream to the supplied sink.
/// </summary>
/// <remarks>
/// Two entry points:
/// <list type="bullet">
///   <item>
///     <see cref="RunWithBackendAsync"/> — test-friendly: takes an already-constructed
///     <see cref="IGameBackend"/>. In tests we plug an in-process wrapper around <c>PokerGame</c>;
///     in production we pass an <see cref="McpGameClient"/>.
///   </item>
///   <item>
///     <see cref="RunAsync"/> — production wrapper that owns the <see cref="McpGameClient"/>
///     lifecycle (spawn + dispose) around the inner backend call. The CLI in Task 3.7 uses this.
///   </item>
/// </list>
/// </remarks>
public static class BattleOrchestrator
{
    /// <summary>
    /// Run a full match against the supplied backend. Does NOT spawn or own the backend.
    /// </summary>
    public static async System.Threading.Tasks.Task RunWithBackendAsync(
        BattleConfig config,
        IReadOnlyDictionary<string, AgentProfile> profilesById,
        System.Func<AgentProfile, IAgentClient> agentClientFactory,
        IGameBackend mcp,
        IBattleEventSink sink,
        System.TimeProvider time,
        string battleId,
        System.Threading.CancellationToken ct = default)
    {
        var seats = config.Seats.Select(s => s.Seat).ToArray();
        var stacks = config.Seats.ToDictionary(s => s.Seat, _ => config.StartingStack);
        var profilesBySeat = config.Seats.ToDictionary(s => s.Seat, s => profilesById[s.Agent]);

        await mcp.ConfigureGameAsync(
            seats,
            seats.Select(s => stacks[s]).ToArray(),
            seats.Select(s => profilesBySeat[s].DisplayName).ToArray(),
            sb: config.Blinds.Small, bb: config.Blinds.Big,
            btn: seats[0],
            seed: System.Random.Shared.Next(),
            ct);

        // Build sessions per seat
        var sessions = new Dictionary<int, AgentSession>();
        var toolDefs = BuildToolDefinitions();
        foreach (var s in config.Seats)
        {
            var profile = profilesById[s.Agent];
            var systemPrompt = PromptBuilder.System(profile, s.Seat);
            sessions[s.Seat] = new AgentSession(profile.Id, profile.DisplayName, agentClientFactory(profile), toolDefs, systemPrompt);
        }

        await sink.WriteAsync(new BattleEvent.BattleStarted(
            time.GetUtcNow(),
            battleId,
            ConfigSnapshot: System.Text.Json.JsonSerializer.Serialize(config, AgentBattle.Domain.Json.BattleEventJsonOptions.Default),
            Agents: config.Seats.Select(s => new SeatedAgent(s.Seat, profilesById[s.Agent].Id, profilesById[s.Agent].DisplayName)).ToArray()), ct);

        var runner = new TurnRunner(sink, time);

        for (var hand = 1; hand <= config.Hands; hand++)
        {
            // If only one player still has chips, the match is effectively over —
            // PokerGame.StartHand would throw "Need at least 2 players with chips".
            // End the battle gracefully instead.
            var aliveProbe = await mcp.GetMyStateAsync(seats[0], ct);
            var seatsWithChips = aliveProbe.Seats.Count(s => s.Stack > 0);
            if (seatsWithChips < 2) break;

            await mcp.StartHandAsync(ct);

            // Probe one seat's state to learn inactive seats / community / starting current seat.
            // HandStarted button/SB/BB are placeholder zeros — the MCP server doesn't currently
            // expose them through a tool. Documented as a concern for the replay viewer.
            var probeState = await mcp.GetMyStateAsync(seats[0], ct);
            var inactiveSeats = probeState.Seats.Where(s => s.IsInactive).Select(s => s.Seat).ToArray();

            await sink.WriteAsync(new BattleEvent.HandStarted(
                time.GetUtcNow(),
                HandNo: hand,
                ButtonSeat: 0,   // TODO: surface from MCP server when tool added
                SbSeat: 0,
                BbSeat: 0,
                InactiveSeats: inactiveSeats), ct);

            // God-view reveal — log everyone's hole cards (used by replay viewer in spectator mode).
            var reveals = await mcp.GodViewRevealAsync(ct);
            await sink.WriteAsync(new BattleEvent.HoleCardsDealt(
                time.GetUtcNow(), hand,
                Deals: reveals.Select(kv => new HoleCardDeal(kv.Key, kv.Value)).ToArray()), ct);

            var lastStreet = probeState.Street;
            var lastCommunityCount = probeState.Community.Count;

            while (mcp.CurrentStreet != Street.Showdown)
            {
                var seat = mcp.CurrentSeat;
                var session = sessions[seat];

                await runner.RunOneTurnAsync(
                    seat: seat,
                    session: session,
                    getState: (s, c) => mcp.GetMyStateAsync(s, c),
                    apply: (a, c) => mcp.ApplyAsync(a, c),
                    ct: ct);

                // Detect street advance — peek state again. Community is monotonically extended
                // within a hand so a count growth signals the dealer dealt cards for the new street.
                var probe = await mcp.GetMyStateAsync(seats[0], ct);
                if (probe.Street != lastStreet && probe.Street != Street.Showdown)
                {
                    var newCards = probe.Community.Skip(lastCommunityCount).ToArray();
                    if (newCards.Length > 0)
                    {
                        await sink.WriteAsync(new BattleEvent.CommunityDealt(time.GetUtcNow(), hand, probe.Street, newCards), ct);
                        lastCommunityCount = probe.Community.Count;
                    }
                    lastStreet = probe.Street;
                }
                else if (probe.Community.Count > lastCommunityCount)
                {
                    // Edge case: all-in fast-forward dealt multiple streets at once.
                    var newCards = probe.Community.Skip(lastCommunityCount).ToArray();
                    await sink.WriteAsync(new BattleEvent.CommunityDealt(time.GetUtcNow(), hand, probe.Street, newCards), ct);
                    lastCommunityCount = probe.Community.Count;
                    lastStreet = probe.Street;
                }
            }

            // Showdown
            var showdown = await mcp.ResolveShowdownAsync(ct);
            await sink.WriteAsync(new BattleEvent.Showdown(
                time.GetUtcNow(), hand,
                Reveals: showdown.Reveals.Select(kv => new HoleCardDeal(kv.Key, kv.Value)).ToArray(),
                Winners: showdown.Winners.Select(w => new PotWinner(w.Seat, w.Pot, w.Description)).ToArray()), ct);
            await sink.WriteAsync(new BattleEvent.HandEnded(time.GetUtcNow(), hand, showdown.Stacks), ct);
        }

        // Battle ended — read final stacks from any seat's view.
        var finalState = await mcp.GetMyStateAsync(seats[0], ct);
        var finalStacks = finalState.Seats.ToDictionary(s => s.Seat, s => s.Stack);
        var ranking = finalStacks.OrderByDescending(kv => kv.Value)
            .Select(kv => new RankEntry(kv.Key, kv.Value, profilesBySeat[kv.Key].Id))
            .ToArray();
        await sink.WriteAsync(new BattleEvent.BattleEnded(time.GetUtcNow(), finalStacks, ranking), ct);
    }

    /// <summary>
    /// Production entry point — spawns and owns an <see cref="McpGameClient"/> for the duration
    /// of the match, then delegates to <see cref="RunWithBackendAsync"/>. Used by the CLI in Task 3.7.
    /// </summary>
    public static async System.Threading.Tasks.Task RunAsync(
        BattleConfig config,
        IReadOnlyDictionary<string, AgentProfile> profilesById,
        System.Func<AgentProfile, IAgentClient> agentClientFactory,
        System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<McpGameClient>> mcpFactory,
        IBattleEventSink sink,
        System.TimeProvider time,
        string battleId,
        System.Threading.CancellationToken ct = default)
    {
        await using var mcp = await mcpFactory(ct);
        await RunWithBackendAsync(config, profilesById, agentClientFactory, mcp, sink, time, battleId, ct);
    }

    private static IReadOnlyList<ToolDefinition> BuildToolDefinitions() =>
    [
        new ToolDefinition("fold", "Fold your hand.", """{"type":"object","properties":{},"required":[]}"""),
        new ToolDefinition("check", "Check (only legal when no bet is outstanding).", """{"type":"object","properties":{},"required":[]}"""),
        new ToolDefinition("call", "Call the current bet.", """{"type":"object","properties":{},"required":[]}"""),
        new ToolDefinition("raise", "Raise to the given total bet level.", """{"type":"object","properties":{"amount":{"type":"integer","description":"Total bet level to raise to."}},"required":["amount"]}"""),
        new ToolDefinition("all_in", "Push all remaining chips into the pot.", """{"type":"object","properties":{},"required":[]}""")
    ];
}
