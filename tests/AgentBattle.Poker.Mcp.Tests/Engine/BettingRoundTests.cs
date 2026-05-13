using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class BettingRoundTests
{
    private static BettingRound NewRound(int[] activeSeats, int startSeat, int bigBlind = 20)
        => new BettingRound(activeSeats, startSeat, currentBet: bigBlind, minRaise: bigBlind);

    [Fact]
    public void Round_starts_open_with_first_seat_to_act()
    {
        var r = NewRound([0, 1, 2], startSeat: 0);
        r.IsClosed.Should().BeFalse();
        r.CurrentSeat.Should().Be(0);
    }

    [Fact]
    public void Check_advances_to_next_seat_when_no_outstanding_bet()
    {
        var r = new BettingRound([0, 1, 2], 0, currentBet: 0, minRaise: 20);
        r.RecordCheck(0);
        r.CurrentSeat.Should().Be(1);
    }

    [Fact]
    public void Round_closes_after_all_seats_call_the_initial_bet()
    {
        var r = NewRound([0, 1, 2], 0);
        r.RecordCall(0, amount: 20);
        r.RecordCall(1, amount: 20);
        r.RecordCheck(2);
        r.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Raise_reopens_action_for_seats_that_already_acted()
    {
        var r = NewRound([0, 1, 2], 0);
        r.RecordCall(0, amount: 20);
        r.RecordRaiseTo(1, totalBet: 60);
        r.CurrentSeat.Should().Be(2);
        r.IsClosed.Should().BeFalse();
        r.RecordCall(2, amount: 60);
        r.CurrentSeat.Should().Be(0);
        r.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Folds_remove_seat_from_active_list()
    {
        var r = NewRound([0, 1, 2], 0);
        r.RecordFold(0);
        r.ActiveSeats.Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
