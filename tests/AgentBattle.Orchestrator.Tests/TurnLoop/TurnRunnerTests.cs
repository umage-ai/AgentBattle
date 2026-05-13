using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Recording;
using AgentBattle.Orchestrator.TurnLoop;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.TurnLoop;

public class TurnRunnerTests
{
    private static PokerState SampleState(LegalActions legal, int seat = 0) => new(
        HandNo: 1, Street: Street.Preflop, Seat: seat,
        HoleCards: [Card.Parse("As"), Card.Parse("Kd")],
        Community: [], MyStack: 1000, MyCurrentBet: 0, Pot: 30, ToCall: 20,
        Seats: [], ActionLog: [], CurrentSeat: seat,
        Legal: legal);

    [Fact]
    public async Task Happy_path_emits_thoughts_then_action()
    {
        var sink = new InMemorySink();
        var legal = new LegalActions(false, true, 20, true, 40, 1000, true);
        var state = SampleState(legal);
        var agent = new ScriptedAgent([("I will call.", new ToolCall("call", "{}"))]);
        var session = new AgentSession("a", "A", agent, [], "sys");
        var runner = new TurnRunner(sink, System.TimeProvider.System);

        await runner.RunOneTurnAsync(seat: 0, session,
            getState: (_, __) => System.Threading.Tasks.Task.FromResult(state),
            apply: (_, __) => System.Threading.Tasks.Task.FromResult<(bool, string?)>((true, null)),
            ct: default);

        sink.Events.Select(e => e.GetType().Name).Should().Equal(
            nameof(BattleEvent.AgentTurnStarted),
            nameof(BattleEvent.AgentThoughts),
            nameof(BattleEvent.AgentAction));
    }

    [Fact]
    public async Task Rejected_action_is_logged_and_retried()
    {
        var sink = new InMemorySink();
        var legal = new LegalActions(false, true, 20, true, 40, 1000, true);
        var state = SampleState(legal);
        var agent = new ScriptedAgent([
            ("Trying a raise.", new ToolCall("raise", "{\"amount\":15}")),   // below_min_raise
            ("OK, calling.",     new ToolCall("call", "{}"))
        ]);
        var session = new AgentSession("a", "A", agent, [], "sys");
        var runner = new TurnRunner(sink, System.TimeProvider.System);

        var applyCalls = 0;
        await runner.RunOneTurnAsync(0, session,
            (_, __) => System.Threading.Tasks.Task.FromResult(state),
            (a, __) =>
            {
                applyCalls++;
                if (a is PokerAction.Raise) return System.Threading.Tasks.Task.FromResult<(bool, string?)>((false, "below_min_raise"));
                return System.Threading.Tasks.Task.FromResult<(bool, string?)>((true, null));
            },
            ct: default);

        applyCalls.Should().Be(2);
        sink.Events.OfType<BattleEvent.AgentActionRejected>().Should().HaveCount(1);
        sink.Events.OfType<BattleEvent.AgentAction>().Should().HaveCount(1);
        sink.Events.OfType<BattleEvent.AgentAction>().Single().Attempt.Should().Be(2);
    }

    [Fact]
    public async Task Three_rejections_force_check_or_fold()
    {
        var sink = new InMemorySink();
        // Check is NOT legal here, so forced default should be Fold
        var legal = new LegalActions(false, true, 20, true, 40, 1000, true);
        var state = SampleState(legal);
        var agent = new ScriptedAgent([
            ("r1", new ToolCall("raise", "{\"amount\":15}")),
            ("r2", new ToolCall("raise", "{\"amount\":15}")),
            ("r3", new ToolCall("raise", "{\"amount\":15}"))
        ]);
        var session = new AgentSession("a", "A", agent, [], "sys");
        var runner = new TurnRunner(sink, System.TimeProvider.System);

        var forced = (PokerAction?)null;
        await runner.RunOneTurnAsync(0, session,
            (_, __) => System.Threading.Tasks.Task.FromResult(state),
            (a, __) =>
            {
                if (a is PokerAction.Raise) return System.Threading.Tasks.Task.FromResult<(bool, string?)>((false, "below_min_raise"));
                forced = a;
                return System.Threading.Tasks.Task.FromResult<(bool, string?)>((true, null));
            },
            ct: default);

        sink.Events.OfType<BattleEvent.AgentActionRejected>().Should().HaveCount(3);
        var final = sink.Events.OfType<BattleEvent.AgentAction>().Single();
        final.AutoReason.Should().Be("retries_exhausted");
        forced.Should().BeOfType<PokerAction.Fold>();  // check not legal here
    }

    [Fact]
    public async Task Three_rejections_with_check_legal_forces_check()
    {
        var sink = new InMemorySink();
        var legal = new LegalActions(CanCheck: true, false, 0, true, 20, 1000, CanFold: false);
        var state = SampleState(legal);
        var agent = new ScriptedAgent([
            ("r1", new ToolCall("raise", "{\"amount\":5}")),
            ("r2", new ToolCall("raise", "{\"amount\":5}")),
            ("r3", new ToolCall("raise", "{\"amount\":5}"))
        ]);
        var session = new AgentSession("a", "A", agent, [], "sys");
        var runner = new TurnRunner(sink, System.TimeProvider.System);

        PokerAction? forced = null;
        await runner.RunOneTurnAsync(0, session,
            (_, __) => System.Threading.Tasks.Task.FromResult(state),
            (a, __) =>
            {
                if (a is PokerAction.Raise) return System.Threading.Tasks.Task.FromResult<(bool, string?)>((false, "below_min_raise"));
                forced = a;
                return System.Threading.Tasks.Task.FromResult<(bool, string?)>((true, null));
            },
            ct: default);

        forced.Should().BeOfType<PokerAction.Check>();
    }

    [Fact]
    public async Task Reply_with_no_tool_call_counts_as_rejection()
    {
        var sink = new InMemorySink();
        var legal = new LegalActions(false, true, 20, true, 40, 1000, true);
        var state = SampleState(legal);
        var agent = new ScriptedAgent([
            ("I'm just thinking out loud.", null),
            ("OK now calling.", new ToolCall("call", "{}"))
        ]);
        var session = new AgentSession("a", "A", agent, [], "sys");
        var runner = new TurnRunner(sink, System.TimeProvider.System);

        await runner.RunOneTurnAsync(0, session,
            (_, __) => System.Threading.Tasks.Task.FromResult(state),
            (_, __) => System.Threading.Tasks.Task.FromResult<(bool, string?)>((true, null)),
            ct: default);

        sink.Events.OfType<BattleEvent.AgentActionRejected>().Should().HaveCount(1);
        sink.Events.OfType<BattleEvent.AgentActionRejected>().Single().Reason.Should().Be("no_tool_call");
    }

    // Stub IAgentClient that replies with a scripted sequence of (content, toolCall).
    private sealed class ScriptedAgent(IReadOnlyList<(string Content, ToolCall? Tool)> replies) : IAgentClient
    {
        private int _i;
        public System.Threading.Tasks.Task<AgentReply> ChatAsync(
            IReadOnlyList<AgentMessage> messages, IReadOnlyList<ToolDefinition> tools, System.Threading.CancellationToken ct = default)
        {
            var (content, tool) = replies[_i++];
            var calls = tool is null ? (IReadOnlyList<ToolCall>)System.Array.Empty<ToolCall>() : [tool];
            return System.Threading.Tasks.Task.FromResult(new AgentReply(content, calls, 100));
        }
    }

    private sealed class InMemorySink : IBattleEventSink
    {
        public List<BattleEvent> Events { get; } = [];
        public System.Threading.Tasks.Task WriteAsync(BattleEvent e, System.Threading.CancellationToken ct = default) { Events.Add(e); return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }
}
