using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Mcp;
using AgentBattle.Orchestrator.Recording;
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests;

public class BattleOrchestratorTests
{
    [Fact]
    public async Task Runs_a_short_match_and_emits_complete_event_stream()
    {
        // Set up: 3 seats, 3 stubbed always-check-or-call agents, 5-hand match.
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = new("a", "A", "http://stub", "m", "NONE"),
            ["b"] = new("b", "B", "http://stub", "m", "NONE"),
            ["c"] = new("c", "C", "http://stub", "m", "NONE")
        };
        var config = new BattleConfig(
            Game: "poker-6max",
            Hands: 5,
            StartingStack: 1000,
            Blinds: new BlindsConfig(10, 20),
            Seats: new[]
            {
                new SeatAssignment(0, "a"),
                new SeatAssignment(1, "b"),
                new SeatAssignment(2, "c"),
            });

        // Real in-process PokerGame as the "MCP" backing.
        var game = new PokerGame(
            seats: [0, 1, 2],
            startingStacks: new() { [0] = 1000, [1] = 1000, [2] = 1000 },
            agentNames: new() { [0] = "A", [1] = "B", [2] = "C" },
            smallBlind: 10, bigBlind: 20,
            buttonSeat: 2, deckSeed: 42);

        var fakeMcp = new FakeMcpClient(game);
        var sink = new InMemorySink();

        await BattleOrchestrator.RunWithBackendAsync(
            config, profiles,
            agentClientFactory: _ => new ScriptedCheckOrCallAgent(),
            mcp: fakeMcp,
            sink: sink,
            time: System.TimeProvider.System,
            battleId: "test123",
            ct: default);

        // Assertions
        sink.Events.OfType<BattleEvent.BattleStarted>().Should().HaveCount(1);
        sink.Events.OfType<BattleEvent.BattleEnded>().Should().HaveCount(1);
        sink.Events.OfType<BattleEvent.HandStarted>().Should().HaveCount(5);
        sink.Events.OfType<BattleEvent.HandEnded>().Should().HaveCount(5);

        // Chip conservation: every hand_ended should sum to 3000.
        foreach (var hand in sink.Events.OfType<BattleEvent.HandEnded>())
            hand.Stacks.Values.Sum().Should().Be(3000, $"hand {hand.HandNo} should conserve chips");

        // Final ranking should be present
        var ended = sink.Events.OfType<BattleEvent.BattleEnded>().Single();
        ended.Ranking.Should().HaveCount(3);
        ended.FinalStacks.Values.Sum().Should().Be(3000);
    }

    // Stub agent: always checks if possible, otherwise calls.
    private sealed class ScriptedCheckOrCallAgent : IAgentClient
    {
        public System.Threading.Tasks.Task<AgentReply> ChatAsync(
            IReadOnlyList<AgentMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            System.Threading.CancellationToken ct = default)
        {
            // Look at the last user message to decide.
            var lastUser = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            ToolCall call;
            if (lastUser.Contains("check"))
                call = new ToolCall("check", "{}");
            else if (lastUser.Contains("call"))
                call = new ToolCall("call", "{}");
            else
                call = new ToolCall("fold", "{}");
            return System.Threading.Tasks.Task.FromResult(new AgentReply("Standard play.", [call], 50));
        }
    }

    // Fake MCP client: wraps a real PokerGame, no process spawning.
    private sealed class FakeMcpClient : IGameBackend
    {
        private readonly PokerGame _game;
        public FakeMcpClient(PokerGame game) { _game = game; }

        public System.Threading.Tasks.Task ConfigureGameAsync(int[] seats, int[] startingStacks, string[] agentNames, int sb, int bb, int btn, int seed, System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<int> StartHandAsync(System.Threading.CancellationToken ct) { _game.StartHand(); return System.Threading.Tasks.Task.FromResult(_game.HandNo); }
        public System.Threading.Tasks.Task<PokerState> GetMyStateAsync(int seat, System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.FromResult(_game.GetMyState(seat));
        public System.Threading.Tasks.Task<(bool Ok, string? Error)> ApplyAsync(PokerAction action, System.Threading.CancellationToken ct)
        {
            var r = _game.Apply(action);
            return System.Threading.Tasks.Task.FromResult((r.Ok, r.Reason));
        }
        public System.Threading.Tasks.Task<McpShowdownResult> ResolveShowdownAsync(System.Threading.CancellationToken ct)
        {
            var s = _game.ResolveShowdown();
            var winners = s.Winners.Select(w => new McpPotWinner(w.Seat, w.Pot, w.Description)).ToArray();
            return System.Threading.Tasks.Task.FromResult(new McpShowdownResult(s.Stacks, winners, s.Reveals));
        }
        public System.Threading.Tasks.Task<IReadOnlyDictionary<int, IReadOnlyList<Card>>> GodViewRevealAsync(System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(_game.CurrentHoleCards());
        public int CurrentSeat => _game.CurrentSeat;
        public Street CurrentStreet => _game.Street;
    }

    private sealed class InMemorySink : IBattleEventSink
    {
        public List<BattleEvent> Events { get; } = [];
        public System.Threading.Tasks.Task WriteAsync(BattleEvent e, System.Threading.CancellationToken ct = default) { Events.Add(e); return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }
}
