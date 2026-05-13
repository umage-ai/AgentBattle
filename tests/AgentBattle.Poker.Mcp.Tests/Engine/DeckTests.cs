using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class DeckTests
{
    [Fact]
    public void Fresh_deck_contains_52_unique_cards()
    {
        var deck = new Deck(seed: 1);
        var drawn = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 52; i++) drawn.Add(deck.Draw().ToString());
        drawn.Count.Should().Be(52);
    }

    [Fact]
    public void Drawing_53_cards_throws()
    {
        var deck = new Deck(seed: 1);
        for (int i = 0; i < 52; i++) deck.Draw();
        var act = () => deck.Draw();
        act.Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Same_seed_produces_same_order()
    {
        var d1 = new Deck(seed: 42);
        var d2 = new Deck(seed: 42);
        for (int i = 0; i < 52; i++)
            d1.Draw().Should().Be(d2.Draw());
    }
}
