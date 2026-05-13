namespace AgentBattle.Domain.Cards;

public readonly record struct Card(Rank Rank, Suit Suit)
{
    public static Card Parse(string s)
    {
        if (s is not { Length: 2 })
            throw new System.FormatException($"Card notation must be 2 chars: '{s}'");
        var rank = s[0] switch
        {
            '2' => Rank.Two, '3' => Rank.Three, '4' => Rank.Four, '5' => Rank.Five,
            '6' => Rank.Six, '7' => Rank.Seven, '8' => Rank.Eight, '9' => Rank.Nine,
            'T' or 't' => Rank.Ten, 'J' or 'j' => Rank.Jack, 'Q' or 'q' => Rank.Queen,
            'K' or 'k' => Rank.King, 'A' or 'a' => Rank.Ace,
            _ => throw new System.FormatException($"Invalid rank char: '{s[0]}'")
        };
        var suit = s[1] switch
        {
            'c' or 'C' => Suit.Clubs, 'd' or 'D' => Suit.Diamonds,
            'h' or 'H' => Suit.Hearts, 's' or 'S' => Suit.Spades,
            _ => throw new System.FormatException($"Invalid suit char: '{s[1]}'")
        };
        return new Card(rank, suit);
    }

    public override string ToString()
    {
        var r = Rank switch
        {
            Rank.Ten => 'T', Rank.Jack => 'J', Rank.Queen => 'Q',
            Rank.King => 'K', Rank.Ace => 'A',
            _ => (char)('0' + (int)Rank)
        };
        var s = Suit switch
        {
            Suit.Clubs => 'c', Suit.Diamonds => 'd',
            Suit.Hearts => 'h', Suit.Spades => 's',
            _ => '?'
        };
        return $"{r}{s}";
    }
}
