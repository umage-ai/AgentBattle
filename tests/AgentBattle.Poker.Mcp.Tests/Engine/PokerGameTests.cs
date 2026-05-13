using AgentBattle.Domain.Poker;
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class PokerGameTests
{
    [Fact]
    public void Three_handed_call_everything_to_showdown_conserves_chips()
    {
        var game = new PokerGame(
            seats: [0, 1, 2],
            startingStacks: new() { [0] = 1000, [1] = 1000, [2] = 1000 },
            agentNames: new() { [0] = "A", [1] = "B", [2] = "C" },
            smallBlind: 10, bigBlind: 20,
            buttonSeat: 0, deckSeed: 1);

        game.StartHand();
        game.Street.Should().Be(Street.Preflop);

        // Drive the hand: each seat checks if legal, otherwise calls.
        // Watch for safety: stop after 50 actions just in case.
        var safety = 0;
        while (game.Street != Street.Showdown && safety++ < 50)
        {
            var seat = game.CurrentSeat;
            var state = game.GetMyState(seat);
            if (state.Legal.CanCheck) game.Apply(new PokerAction.Check(seat));
            else if (state.Legal.CanCall) game.Apply(new PokerAction.Call(seat));
            else game.Apply(new PokerAction.Fold(seat));
        }

        game.Street.Should().Be(Street.Showdown);
        var result = game.ResolveShowdown();
        result.Winners.Should().NotBeEmpty();
        result.Stacks.Values.Sum().Should().Be(3000); // chips conserved
    }

    [Fact]
    public void Heads_up_button_fold_to_BB_loses_small_blind()
    {
        var game = new PokerGame([0, 1], new() { [0] = 1000, [1] = 1000 }, new() { [0] = "A", [1] = "B" }, 10, 20, buttonSeat: 0, deckSeed: 2);
        game.StartHand();
        // Heads-up: button (seat 0) is SB and acts first preflop. Folding gives the pot to BB (seat 1).
        game.CurrentSeat.Should().Be(0);
        game.Apply(new PokerAction.Fold(0));
        game.Street.Should().Be(Street.Showdown);
        var result = game.ResolveShowdown();
        // SB committed 10 chips, BB committed 20; BB wins both. SB ends -10, BB ends +10.
        result.Stacks[0].Should().Be(990);
        result.Stacks[1].Should().Be(1010);
    }
}
