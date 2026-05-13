using AgentBattle.Domain.Cards;

namespace AgentBattle.Poker.Mcp.Engine;

public enum HandCategory
{
    HighCard, Pair, TwoPair, ThreeOfAKind, Straight, Flush, FullHouse, FourOfAKind, StraightFlush
}

public readonly record struct HandRank(HandCategory Category, IReadOnlyList<int> Tiebreak, string Description)
    : System.IComparable<HandRank>
{
    public int CompareTo(HandRank other)
    {
        var c = Category.CompareTo(other.Category);
        if (c != 0) return c;
        for (var i = 0; i < System.Math.Min(Tiebreak.Count, other.Tiebreak.Count); i++)
        {
            var diff = Tiebreak[i].CompareTo(other.Tiebreak[i]);
            if (diff != 0) return diff;
        }
        return 0;
    }
}

public static class HandEvaluator
{
    public static HandRank Evaluate(IReadOnlyList<Card> available)
    {
        if (available.Count < 5) throw new System.ArgumentException("Need at least 5 cards");
        HandRank best = default;
        var first = true;
        foreach (var combo in Combinations(available, 5))
        {
            var rank = ScoreFive(combo);
            if (first || rank.CompareTo(best) > 0) { best = rank; first = false; }
        }
        return best;
    }

    private static HandRank ScoreFive(IReadOnlyList<Card> five)
    {
        var ranks = five.Select(c => (int)c.Rank).OrderDescending().ToArray();
        var suits = five.Select(c => c.Suit).ToArray();
        var isFlush = suits.Distinct().Count() == 1;
        var isStraight = IsStraight(ranks, out var straightHigh);
        var groups = ranks.GroupBy(r => r).Select(g => (Rank: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ThenByDescending(g => g.Rank).ToArray();

        if (isFlush && isStraight) return new(HandCategory.StraightFlush, [straightHigh], $"Straight flush, {RankName(straightHigh)} high");
        if (groups[0].Count == 4) return new(HandCategory.FourOfAKind, [groups[0].Rank, groups[1].Rank], $"Four of a kind, {RankName(groups[0].Rank)}s");
        if (groups[0].Count == 3 && groups[1].Count == 2) return new(HandCategory.FullHouse, [groups[0].Rank, groups[1].Rank], $"Full house, {RankName(groups[0].Rank)}s over {RankName(groups[1].Rank)}s");
        if (isFlush) return new(HandCategory.Flush, ranks, $"Flush, {RankName(ranks[0])} high");
        if (isStraight) return new(HandCategory.Straight, [straightHigh], $"Straight, {RankName(straightHigh)} high");
        if (groups[0].Count == 3) return new(HandCategory.ThreeOfAKind, [groups[0].Rank, groups[1].Rank, groups[2].Rank], $"Three of a kind, {RankName(groups[0].Rank)}s");
        if (groups[0].Count == 2 && groups[1].Count == 2) return new(HandCategory.TwoPair, [groups[0].Rank, groups[1].Rank, groups[2].Rank], $"Two pair, {RankName(groups[0].Rank)}s and {RankName(groups[1].Rank)}s");
        if (groups[0].Count == 2) return new(HandCategory.Pair, [groups[0].Rank, groups[1].Rank, groups[2].Rank, groups[3].Rank], $"Pair of {RankName(groups[0].Rank)}s");
        return new(HandCategory.HighCard, ranks, $"High card {RankName(ranks[0])}");
    }

    private static bool IsStraight(int[] descRanks, out int high)
    {
        // descRanks is 5 ints, sorted desc. Duplicates disqualify.
        if (descRanks.Distinct().Count() != 5) { high = 0; return false; }
        // Wheel: A,5,4,3,2
        if (descRanks[0] == 14 && descRanks[1] == 5 && descRanks[2] == 4 && descRanks[3] == 3 && descRanks[4] == 2)
        { high = 5; return true; }
        for (var i = 1; i < descRanks.Length; i++)
            if (descRanks[i] != descRanks[i - 1] - 1) { high = 0; return false; }
        high = descRanks[0];
        return true;
    }

    private static string RankName(int r) => r switch
    {
        14 => "Ace", 13 => "King", 12 => "Queen", 11 => "Jack", 10 => "Ten",
        _ => r.ToString()
    };

    private static IEnumerable<IReadOnlyList<Card>> Combinations(IReadOnlyList<Card> cards, int k)
    {
        var idx = new int[k];
        for (var i = 0; i < k; i++) idx[i] = i;
        while (true)
        {
            yield return idx.Select(i => cards[i]).ToArray();
            var p = k - 1;
            while (p >= 0 && idx[p] == cards.Count - k + p) p--;
            if (p < 0) yield break;
            idx[p]++;
            for (var i = p + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
        }
    }
}
