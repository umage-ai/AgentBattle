using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Agents;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_includes_seat_and_persona()
    {
        var profile = new AgentProfile("gpt-5", "GPT-5", "https://x", "gpt-5", "K", 0.7, 1500, 60, "Play tight.");
        var s = PromptBuilder.System(profile, seat: 3);
        s.Should().Contain("seat 3").And.Contain("Play tight.");
    }

    [Fact]
    public void Turn_message_includes_legal_actions_and_hole_cards()
    {
        var state = new PokerState(
            HandNo: 1, Street: Street.Preflop, Seat: 2,
            HoleCards: [Card.Parse("As"), Card.Parse("Kd")],
            Community: [], MyStack: 980, MyCurrentBet: 0, Pot: 30, ToCall: 20,
            Seats: [], ActionLog: [], CurrentSeat: 2,
            Legal: new LegalActions(false, true, 20, true, 40, 980, true));
        var msg = PromptBuilder.Turn(state, retryError: null);
        msg.Should().Contain("As").And.Contain("Kd").And.Contain("To call: 20")
           .And.Contain("call").And.Contain("raise").And.Contain("fold")
           .And.NotContain("check");
    }

    [Fact]
    public void Turn_message_surfaces_retry_error_when_present()
    {
        var state = new PokerState(1, Street.Preflop, 0, [], [], 1000, 0, 30, 20,
            [], [], 0, new LegalActions(false, true, 20, true, 40, 1000, true));
        var msg = PromptBuilder.Turn(state, retryError: "below_min_raise");
        msg.Should().Contain("below_min_raise");
    }
}
