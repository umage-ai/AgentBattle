using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Mcp;

public class StateProjectionTests
{
    [Fact]
    public void GetMyState_only_reveals_requesting_seats_hole_cards()
    {
        var game = new PokerGame(
            seats: [0, 1, 2],
            startingStacks: new() { [0] = 1000, [1] = 1000, [2] = 1000 },
            agentNames: new() { [0] = "A", [1] = "B", [2] = "C" },
            smallBlind: 10, bigBlind: 20,
            buttonSeat: 0, deckSeed: 7);
        game.StartHand();

        var stateForSeat1 = game.GetMyState(1);

        // Seat 1 sees its own two hole cards.
        stateForSeat1.HoleCards.Should().HaveCount(2);

        // No part of the SeatSummary payload exposes hole-card data for other seats.
        // Reflective check: no property on SeatSummary should be named anything card-like.
        var seatSummaryProps = typeof(AgentBattle.Domain.Poker.SeatSummary)
            .GetProperties()
            .Select(p => p.Name);
        seatSummaryProps.Should().NotContain(p =>
            p.Equals("HoleCards", System.StringComparison.OrdinalIgnoreCase) ||
            p.Equals("Cards", System.StringComparison.OrdinalIgnoreCase));
    }
}
