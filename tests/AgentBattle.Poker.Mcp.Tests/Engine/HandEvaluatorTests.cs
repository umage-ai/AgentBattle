using AgentBattle.Domain.Cards;
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class HandEvaluatorTests
{
    private static HandRank Eval(params string[] cards)
        => HandEvaluator.Evaluate([.. System.Linq.Enumerable.Select(cards, Card.Parse)]);

    [Fact] public void Detects_royal_flush()      => Eval("As","Ks","Qs","Js","Ts","2c","3d").Category.Should().Be(HandCategory.StraightFlush);
    [Fact] public void Detects_quads()            => Eval("Ah","Ad","Ac","As","2d","3c","4s").Category.Should().Be(HandCategory.FourOfAKind);
    [Fact] public void Detects_full_house()       => Eval("Ah","Ad","Ac","Kh","Kd","2c","3s").Category.Should().Be(HandCategory.FullHouse);
    [Fact] public void Detects_flush()            => Eval("Ah","Th","8h","6h","2h","3c","3s").Category.Should().Be(HandCategory.Flush);
    [Fact] public void Detects_straight()         => Eval("9h","8d","7c","6s","5h","Kd","Qc").Category.Should().Be(HandCategory.Straight);
    [Fact] public void Detects_wheel_A_to_5()     => Eval("Ah","2d","3c","4s","5h","Kd","Qc").Category.Should().Be(HandCategory.Straight);
    [Fact] public void Detects_trips()            => Eval("9h","9d","9c","Ks","Qh","Jc","2s").Category.Should().Be(HandCategory.ThreeOfAKind);
    [Fact] public void Detects_two_pair()         => Eval("9h","9d","8c","8s","Kh","Jc","2s").Category.Should().Be(HandCategory.TwoPair);
    [Fact] public void Detects_pair()             => Eval("9h","9d","Kc","8s","6h","Jc","2s").Category.Should().Be(HandCategory.Pair);
    [Fact] public void Detects_high_card()        => Eval("Ah","Kd","9c","8s","6h","Jc","2s").Category.Should().Be(HandCategory.HighCard);

    [Fact]
    public void Higher_pair_beats_lower_pair()
    {
        var aces = Eval("Ah","Ad","Kc","8s","6h","Jc","2s");
        var nines = Eval("9h","9d","Kc","8s","6h","Jc","2s");
        aces.CompareTo(nines).Should().BePositive();
    }

    [Fact]
    public void Kicker_breaks_ties_for_same_pair()
    {
        var withK = Eval("9h","9d","Kc","8s","6h","Jc","2s");
        var withQ = Eval("9h","9d","Qc","8s","6h","Jc","2s");
        withK.CompareTo(withQ).Should().BePositive();
    }
}
