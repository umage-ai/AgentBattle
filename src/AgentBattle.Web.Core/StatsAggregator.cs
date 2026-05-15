using AgentBattle.Domain.Battles;

namespace AgentBattle.Web.Services;

public sealed record StatsSnapshot(
    IReadOnlyList<ModelStats> Models,
    IReadOnlyList<AgentStats> Agents,
    IReadOnlyList<MatchupStats> ModelMatchups,
    IReadOnlyList<MatchupStats> AgentMatchups);

public sealed record ModelStats(
    string Slug,
    string DisplayName,
    int Battles,
    double Wins,
    double AverageChipShare,
    System.DateTimeOffset LastBattleAt);

public sealed record AgentStats(
    string Slug,
    string DisplayName,
    string? ModelDisplayName,
    int Battles,
    double Wins,
    double AverageChipShare,
    System.DateTimeOffset LastBattleAt);

public sealed record MatchupStats(
    string ASlug,
    string ADisplayName,
    string BSlug,
    string BDisplayName,
    int BattleCount,
    double AWins,
    double BWins,
    IReadOnlyList<string> BattleIds);

public sealed class StatsAggregator
{
    private const string UnknownModelSlug = "unknown";

    public StatsSnapshot Build(
        IReadOnlyList<BattleSummary> completedBattles,
        IReadOnlyDictionary<string, AgentProfile> agentsById)
    {
        var battles = completedBattles.Where(b => b.IsComplete && b.Ranking.Count > 0).ToArray();

        var modelAgg = new Dictionary<string, ModelAccumulator>();
        var agentAgg = new Dictionary<string, AgentAccumulator>();
        var modelMatch = new Dictionary<(string, string), MatchupAccumulator>();
        var agentMatch = new Dictionary<(string, string), MatchupAccumulator>();

        foreach (var battle in battles)
        {
            var rankBySeat = battle.Ranking
                .OrderByDescending(r => r.Chips)
                .Select((r, i) => (r.Seat, r.Chips, Rank: i, r.AgentId))
                .ToDictionary(t => t.Seat);

            int topChips = battle.Ranking.Max(r => r.Chips);
            int topCount = battle.Ranking.Count(r => r.Chips == topChips);
            double winShare = 1.0 / topCount;

            var resolved = new List<SeatEntry>();
            foreach (var sa in battle.SeatedAgents)
            {
                agentsById.TryGetValue(sa.Id, out var profile);
                var modelDisplay = profile?.Model ?? UnknownModelSlug;
                var modelSlug = profile != null ? ModelSlug.For(profile.Model) : UnknownModelSlug;
                var agentSlug = ModelSlug.For(sa.DisplayName);
                if (!rankBySeat.TryGetValue(sa.Seat, out var rinfo)) continue;
                resolved.Add(new SeatEntry(
                    Seat: sa.Seat,
                    AgentId: sa.Id,
                    AgentDisplay: sa.DisplayName,
                    AgentSlug: agentSlug,
                    ModelDisplay: modelDisplay,
                    ModelSlug: modelSlug,
                    Chips: rinfo.Chips,
                    IsTopChips: rinfo.Chips == topChips));
            }

            foreach (var e in resolved)
            {
                if (!agentAgg.TryGetValue(e.AgentSlug, out var acc))
                {
                    acc = new AgentAccumulator(e.AgentSlug, e.AgentDisplay, e.ModelDisplay);
                    agentAgg[e.AgentSlug] = acc;
                }
                acc.Battles++;
                if (e.IsTopChips) acc.Wins += winShare;
                if (battle.StartingStack > 0)
                    acc.ChipShareSum += (double)e.Chips / battle.StartingStack;
                acc.ChipShareCount++;
                if (battle.StartedAt > acc.LastBattleAt) acc.LastBattleAt = battle.StartedAt;
            }

            var modelsThisBattle = resolved
                .Where(e => e.ModelSlug != UnknownModelSlug)
                .GroupBy(e => e.ModelSlug)
                .ToArray();

            foreach (var grp in modelsThisBattle)
            {
                var first = grp.First();
                if (!modelAgg.TryGetValue(grp.Key, out var acc))
                {
                    acc = new ModelAccumulator(grp.Key, first.ModelDisplay);
                    modelAgg[grp.Key] = acc;
                }
                acc.Battles++;
                acc.Wins += grp.Sum(e => e.IsTopChips ? winShare : 0.0);
                if (battle.StartingStack > 0)
                {
                    acc.ChipShareSum += grp.Sum(e => (double)e.Chips / battle.StartingStack);
                    acc.ChipShareCount += grp.Count();
                }
                if (battle.StartedAt > acc.LastBattleAt) acc.LastBattleAt = battle.StartedAt;
            }

            var distinctModels = modelsThisBattle.Select(g => (Slug: g.Key, Display: g.First().ModelDisplay, BestChips: g.Max(e => e.Chips))).ToArray();
            for (int i = 0; i < distinctModels.Length; i++)
            {
                for (int j = i + 1; j < distinctModels.Length; j++)
                {
                    var (slugA, slugB) = string.CompareOrdinal(distinctModels[i].Slug, distinctModels[j].Slug) < 0
                        ? (distinctModels[i].Slug, distinctModels[j].Slug)
                        : (distinctModels[j].Slug, distinctModels[i].Slug);
                    var (dispA, dispB) = slugA == distinctModels[i].Slug
                        ? (distinctModels[i].Display, distinctModels[j].Display)
                        : (distinctModels[j].Display, distinctModels[i].Display);
                    var chipsA = slugA == distinctModels[i].Slug ? distinctModels[i].BestChips : distinctModels[j].BestChips;
                    var chipsB = slugB == distinctModels[i].Slug ? distinctModels[i].BestChips : distinctModels[j].BestChips;

                    if (!modelMatch.TryGetValue((slugA, slugB), out var acc))
                    {
                        acc = new MatchupAccumulator(slugA, dispA, slugB, dispB);
                        modelMatch[(slugA, slugB)] = acc;
                    }
                    acc.BattleIds.Add(battle.BattleId);
                    if (chipsA > chipsB) acc.AWins += 1.0;
                    else if (chipsB > chipsA) acc.BWins += 1.0;
                    else { acc.AWins += 0.5; acc.BWins += 0.5; }
                }
            }

            for (int i = 0; i < resolved.Count; i++)
            {
                for (int j = i + 1; j < resolved.Count; j++)
                {
                    var ei = resolved[i];
                    var ej = resolved[j];
                    if (ei.AgentSlug == ej.AgentSlug) continue;
                    var (slugA, slugB) = string.CompareOrdinal(ei.AgentSlug, ej.AgentSlug) < 0
                        ? (ei.AgentSlug, ej.AgentSlug)
                        : (ej.AgentSlug, ei.AgentSlug);
                    var (dispA, dispB) = slugA == ei.AgentSlug ? (ei.AgentDisplay, ej.AgentDisplay) : (ej.AgentDisplay, ei.AgentDisplay);
                    var chipsA = slugA == ei.AgentSlug ? ei.Chips : ej.Chips;
                    var chipsB = slugB == ei.AgentSlug ? ei.Chips : ej.Chips;

                    if (!agentMatch.TryGetValue((slugA, slugB), out var acc))
                    {
                        acc = new MatchupAccumulator(slugA, dispA, slugB, dispB);
                        agentMatch[(slugA, slugB)] = acc;
                    }
                    acc.BattleIds.Add(battle.BattleId);
                    if (chipsA > chipsB) acc.AWins += 1.0;
                    else if (chipsB > chipsA) acc.BWins += 1.0;
                    else { acc.AWins += 0.5; acc.BWins += 0.5; }
                }
            }
        }

        return new StatsSnapshot(
            Models: modelAgg.Values.Select(a => a.ToStats()).OrderByDescending(m => SafeRate(m.Wins, m.Battles)).ToArray(),
            Agents: agentAgg.Values.Select(a => a.ToStats()).OrderByDescending(a => SafeRate(a.Wins, a.Battles)).ToArray(),
            ModelMatchups: modelMatch.Values.Select(m => m.ToStats()).ToArray(),
            AgentMatchups: agentMatch.Values.Select(m => m.ToStats()).ToArray());
    }

    private static double SafeRate(double wins, int battles) => battles == 0 ? 0 : wins / battles;

    private sealed record SeatEntry(
        int Seat,
        string AgentId,
        string AgentDisplay,
        string AgentSlug,
        string ModelDisplay,
        string ModelSlug,
        int Chips,
        bool IsTopChips);

    private sealed class ModelAccumulator(string slug, string display)
    {
        public string Slug = slug;
        public string Display = display;
        public int Battles;
        public double Wins;
        public double ChipShareSum;
        public int ChipShareCount;
        public System.DateTimeOffset LastBattleAt = System.DateTimeOffset.MinValue;
        public ModelStats ToStats() => new(Slug, Display, Battles, Wins,
            ChipShareCount == 0 ? 0 : ChipShareSum / ChipShareCount, LastBattleAt);
    }

    private sealed class AgentAccumulator(string slug, string display, string? modelDisplay)
    {
        public string Slug = slug;
        public string Display = display;
        public string? ModelDisplay = modelDisplay;
        public int Battles;
        public double Wins;
        public double ChipShareSum;
        public int ChipShareCount;
        public System.DateTimeOffset LastBattleAt = System.DateTimeOffset.MinValue;
        public AgentStats ToStats() => new(Slug, Display, ModelDisplay, Battles, Wins,
            ChipShareCount == 0 ? 0 : ChipShareSum / ChipShareCount, LastBattleAt);
    }

    private sealed class MatchupAccumulator(string aSlug, string aDisplay, string bSlug, string bDisplay)
    {
        public string ASlug = aSlug;
        public string ADisplay = aDisplay;
        public string BSlug = bSlug;
        public string BDisplay = bDisplay;
        public double AWins;
        public double BWins;
        public List<string> BattleIds = [];
        public MatchupStats ToStats() => new(ASlug, ADisplay, BSlug, BDisplay,
            BattleIds.Count, AWins, BWins, BattleIds);
    }
}
