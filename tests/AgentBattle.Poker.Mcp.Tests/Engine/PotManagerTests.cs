using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class PotManagerTests
{
    [Fact]
    public void Single_pot_when_no_one_is_short()
    {
        var pots = PotManager.BuildPots(new() { [0] = 100, [1] = 100, [2] = 100 }, foldedSeats: new());
        pots.Should().HaveCount(1);
        pots[0].Amount.Should().Be(300);
        pots[0].EligibleSeats.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    [Fact]
    public void Side_pot_when_one_player_short_all_in()
    {
        var pots = PotManager.BuildPots(new() { [0] = 40, [1] = 100, [2] = 100 }, foldedSeats: new());
        pots.Should().HaveCount(2);
        pots[0].Amount.Should().Be(120);
        pots[0].EligibleSeats.Should().BeEquivalentTo(new[] { 0, 1, 2 });
        pots[1].Amount.Should().Be(120);
        pots[1].EligibleSeats.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Folded_seats_contribute_but_are_not_eligible()
    {
        var pots = PotManager.BuildPots(new() { [0] = 50, [1] = 100, [2] = 100 }, foldedSeats: new() { 0 });
        pots.Sum(p => p.Amount).Should().Be(250);
        pots.SelectMany(p => p.EligibleSeats).Should().NotContain(0);
    }
}
