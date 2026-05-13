namespace AgentBattle.Poker.Mcp.Engine;

public sealed record SidePot(int Amount, IReadOnlyList<int> EligibleSeats);

public static class PotManager
{
    public static IReadOnlyList<SidePot> BuildPots(Dictionary<int, int> contributions, HashSet<int> foldedSeats)
    {
        var result = new List<SidePot>();
        var working = new Dictionary<int, int>(contributions);
        while (working.Values.Any(v => v > 0))
        {
            var floor = working.Where(kv => kv.Value > 0).Min(kv => kv.Value);
            var contributors = working.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToArray();
            var amount = floor * contributors.Length;
            var eligible = contributors.Where(s => !foldedSeats.Contains(s)).ToArray();
            result.Add(new SidePot(amount, eligible));
            foreach (var s in contributors) working[s] -= floor;
        }
        return result;
    }
}
