using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Mcp;
using FluentAssertions;

namespace AgentBattle.Orchestrator.Tests.Mcp;

public class McpGameClientTests
{
    // Manual integration smoke test. Flip the Skip off to run locally — verifies
    // the stdio transport + tool-call wiring against the built server exe. Kept
    // skipped in CI because it depends on a prior `dotnet build` of the server.
    // The full battle loop in Task 3.6 will exercise this end-to-end.
    [Fact(Skip = "Manual integration smoke test — flip off to run locally; verified passing against built server exe")]
    public async System.Threading.Tasks.Task Heads_up_all_checks_round_trip()
    {
        // Locate the server exe relative to repo root.
        var here = System.AppContext.BaseDirectory;
        var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(here, "..", "..", "..", "..", ".."));
        var serverExe = System.IO.Path.Combine(
            repoRoot, "src", "AgentBattle.Poker.Mcp", "bin", "Debug", "net10.0", "AgentBattle.Poker.Mcp.exe");
        System.IO.File.Exists(serverExe).Should().BeTrue($"server exe expected at {serverExe}");

        await using var client = await McpGameClient.SpawnAsync(serverExe, System.Array.Empty<string>());

        await client.ConfigureGameAsync(
            seats: [0, 1],
            startingStacks: [1000, 1000],
            agentNames: ["A", "B"],
            smallBlind: 10, bigBlind: 20, buttonSeat: 0, deckSeed: 42);

        var handNo = await client.StartHandAsync();
        handNo.Should().Be(1);

        // Heads-up: button (0) is SB, acts first. SB calls, BB checks → flop, then check-check
        // through to showdown. We loop until showdown, taking the cheapest legal action.
        var safety = 0;
        while (safety++ < 30)
        {
            var state = await client.GetMyStateAsync(await GetCurrentSeat(client));
            if (state.Street == Street.Showdown) break;
            PokerAction next = state.Legal.CanCheck
                ? new PokerAction.Check(state.Seat)
                : state.Legal.CanCall ? new PokerAction.Call(state.Seat)
                : new PokerAction.Fold(state.Seat);
            var (ok, err) = await client.ApplyAsync(next);
            ok.Should().BeTrue($"action {next} should be legal; got error: {err}");
        }

        var result = await client.ResolveShowdownAsync();
        result.Winners.Should().NotBeEmpty();
        result.Stacks.Values.Sum().Should().Be(2000);

        var godView = await client.GodViewRevealAsync();
        godView.Should().ContainKeys(0, 1);
    }

    private static async System.Threading.Tasks.Task<int> GetCurrentSeat(McpGameClient client)
    {
        // We don't know which seat is current until we ask — use seat 0 just to read state,
        // then check CurrentSeat. PokerState reports CurrentSeat for everyone.
        var s = await client.GetMyStateAsync(0);
        return s.CurrentSeat;
    }
}
