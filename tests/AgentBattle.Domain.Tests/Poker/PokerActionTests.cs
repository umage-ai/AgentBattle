using AgentBattle.Domain.Poker;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Domain.Tests.Poker;

public class PokerActionTests
{
    [Fact]
    public void Fold_action_carries_seat_and_no_amount()
    {
        PokerAction action = new PokerAction.Fold(Seat: 3);
        action.Seat.Should().Be(3);
        action.Should().BeOfType<PokerAction.Fold>();
    }

    [Fact]
    public void Raise_action_carries_amount_as_total_bet_level()
    {
        PokerAction action = new PokerAction.Raise(Seat: 2, Amount: 60);
        action.Should().BeOfType<PokerAction.Raise>()
            .Which.Amount.Should().Be(60);
    }

    [Fact]
    public void LegalActions_describes_what_a_seat_can_do()
    {
        var legal = new LegalActions(
            CanCheck: false, CanCall: true, CallAmount: 40,
            CanRaise: true, MinRaiseTotal: 80, MaxRaiseTotal: 500,
            CanFold: true);
        legal.CanCheck.Should().BeFalse();
        legal.CallAmount.Should().Be(40);
        legal.MinRaiseTotal.Should().Be(80);
    }
}
