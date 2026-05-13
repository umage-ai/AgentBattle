using AgentBattle.Domain.Cards;

namespace AgentBattle.Poker.Mcp.Engine;

public sealed class Deck
{
    private readonly Card[] _cards = new Card[52];
    private int _next;

    public Deck(int seed)
    {
        var i = 0;
        foreach (var r in System.Enum.GetValues<Rank>())
        foreach (var s in System.Enum.GetValues<Suit>())
            _cards[i++] = new Card(r, s);

        // Fisher–Yates with a seeded Random for reproducibility.
        var rng = new System.Random(seed);
        for (var k = _cards.Length - 1; k > 0; k--)
        {
            var j = rng.Next(k + 1);
            (_cards[k], _cards[j]) = (_cards[j], _cards[k]);
        }
    }

    public Card Draw()
    {
        if (_next >= _cards.Length)
            throw new System.InvalidOperationException("Deck exhausted");
        return _cards[_next++];
    }
}
