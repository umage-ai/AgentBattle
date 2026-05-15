using System.Text.Json;
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
        var actionToolDefs = BuildActionToolDefinitions();
        var noteToolDefs = BuildNoteToolDefinitions();
        foreach (var s in config.Seats)
        {
            var profile = profilesById[s.Agent];
            var systemPrompt = PromptBuilder.SystemPrompt(profile, s.Seat);
            sessions[s.Seat] = new AgentSession(profile.Id, profile.DisplayName, agentClientFactory(profile), actionToolDefs, systemPrompt);
        }

        var tracker = new OpponentTracker(seats);
        var notebook = new AgentNotebook(seats);

        await sink.WriteAsync(new BattleEvent.BattleStarted(
            time.GetUtcNow(),
            battleId,
            ConfigSnapshot: System.Text.Json.JsonSerializer.Serialize(config, AgentBattle.Domain.Json.BattleEventJsonOptions.Default),
            Agents: config.Seats.Select(s => new SeatedAgent(s.Seat, profilesById[s.Agent].Id, profilesById[s.Agent].DisplayName)).ToArray()), ct);

        var runner = new TurnRunner(sink, time, tracker, notebook);

        for (var hand = 1; hand <= config.Hands; hand++)
        {
            var aliveProbe = await mcp.GetMyStateAsync(seats[0], ct);
            var seatsWithChips = aliveProbe.Seats.Count(s => s.Stack > 0);
            if (seatsWithChips < 2) break;

            await mcp.StartHandAsync(ct);

            var probeState = await mcp.GetMyStateAsync(seats[0], ct);
            var inactiveSeats = probeState.Seats.Where(s => s.IsInactive).Select(s => s.Seat).ToArray();

            // Engine rotates the button internally; we recover button/SB/BB from the action log,
            // which always starts with the SB post followed by the BB post for the new hand.
            var posts = probeState.ActionLog.Where(a => a.Action == "post" && a.Street == Street.Preflop).Take(2).ToArray();
            var sbSeat = posts.Length > 0 ? posts[0].Seat : seats[0];
            var bbSeat = posts.Length > 1 ? posts[1].Seat : sbSeat;
            var activeSeats = probeState.Seats.Where(s => !s.IsInactive).Select(s => s.Seat).ToHashSet();
            int buttonSeat;
            if (activeSeats.Count <= 2)
            {
                // Heads-up: button is the SB.
                buttonSeat = sbSeat;
            }
            else
            {
                buttonSeat = sbSeat;
                var sbIdx = System.Array.IndexOf(seats, sbSeat);
                for (var step = 1; step <= seats.Length; step++)
                {
                    var cand = seats[((sbIdx - step) % seats.Length + seats.Length) % seats.Length];
                    if (activeSeats.Contains(cand)) { buttonSeat = cand; break; }
                }
            }

            await sink.WriteAsync(new BattleEvent.HandStarted(
                time.GetUtcNow(),
                HandNo: hand,
                ButtonSeat: buttonSeat,
                SbSeat: sbSeat,
                BbSeat: bbSeat,
                InactiveSeats: inactiveSeats), ct);

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

            // Feed showdown into tracker so future hands have memory.
            var winnerSeats = showdown.Winners.Select(w => w.Seat).Distinct().ToArray();
            var finalCommunity = (await mcp.GetMyStateAsync(seats[0], ct)).Community;
            tracker.OnShowdown(hand, Array.Empty<int>(), finalCommunity, showdown.Reveals, winnerSeats);

            // Note-taking pass — each still-active seat gets one chance to write a private note.
            var postState = await mcp.GetMyStateAsync(seats[0], ct);
            foreach (var seatSummary in postState.Seats.Where(s => !s.IsInactive && s.Stack > 0))
            {
                var session = sessions[seatSummary.Seat];
                var notePrompt = PromptBuilder.NoteTurn(
                    handNo: hand,
                    seats: postState.Seats,
                    reveals: showdown.Reveals,
                    winners: winnerSeats,
                    mySeat: seatSummary.Seat,
                    tracker: tracker,
                    notebook: notebook);

                try
                {
                    var reply = await session.SendUserAsync(notePrompt, noteToolDefs, ct);
                    if (reply.ToolCalls.Count > 0)
                    {
                        var call = reply.ToolCalls[0];
                        if (call.Name == "take_note")
                        {
                            var text = ParseNoteText(call.ArgumentsJson);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                notebook.Add(seatSummary.Seat, hand, text);
                                await sink.WriteAsync(new BattleEvent.AgentNote(time.GetUtcNow(), hand, seatSummary.Seat, text), ct);
                            }
                        }
                    }
                }
                catch
                {
                    // Notes are best-effort — never let a model hiccup tear down the match.
                }
            }
        }

        var finalState = await mcp.GetMyStateAsync(seats[0], ct);
        var finalStacks = finalState.Seats.ToDictionary(s => s.Seat, s => s.Stack);
        var ranking = finalStacks.OrderByDescending(kv => kv.Value)
            .Select(kv => new RankEntry(kv.Key, kv.Value, profilesBySeat[kv.Key].Id))
            .ToArray();
        await sink.WriteAsync(new BattleEvent.BattleEnded(time.GetUtcNow(), finalStacks, ranking), ct);
    }

    /// <summary>
    /// Production entry point — spawns and owns an <see cref="McpGameClient"/> for the duration
    /// of the match, then delegates to <see cref="RunWithBackendAsync"/>.
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

    private static IReadOnlyList<ToolDefinition> BuildActionToolDefinitions() =>
    [
        new ToolDefinition("fold", "Fold your hand.", """{"type":"object","properties":{},"required":[]}"""),
        new ToolDefinition("check", "Check (only legal when no bet is outstanding).", """{"type":"object","properties":{},"required":[]}"""),
        new ToolDefinition("call", "Call the current bet.", """{"type":"object","properties":{},"required":[]}"""),
        new ToolDefinition("raise", "Raise to the given total bet level.", """{"type":"object","properties":{"amount":{"type":"integer","description":"Total bet level to raise to."}},"required":["amount"]}"""),
        new ToolDefinition("all_in", "Push all remaining chips into the pot.", """{"type":"object","properties":{},"required":[]}""")
    ];

    private static IReadOnlyList<ToolDefinition> BuildNoteToolDefinitions() =>
    [
        new ToolDefinition(
            "take_note",
            "Record a short private note for yourself to read on future hands. Use it to capture exploitable patterns about an opponent. One sentence max.",
            """{"type":"object","properties":{"text":{"type":"string","description":"The note text — one sentence about what you've learned."}},"required":["text"]}""")
    ];

    private static string ParseNoteText(string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString() ?? "";
        }
        catch
        {
            // ignore
        }
        return "";
    }
}
