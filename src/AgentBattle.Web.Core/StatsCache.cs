using AgentBattle.Domain.Battles;

namespace AgentBattle.Web.Services;

public sealed class StatsCache(BattleArchive archive, AgentRegistry registry, StatsAggregator aggregator)
{
    private readonly System.Threading.SemaphoreSlim _gate = new(1, 1);
    private string? _signature;
    private StatsSnapshot? _snapshot;

    public async System.Threading.Tasks.Task<StatsSnapshot> GetAsync(System.Threading.CancellationToken ct = default)
    {
        var battles = await archive.ListBattlesAsync(ct);
        var signature = ComputeSignature(battles);

        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot != null && _signature == signature) return _snapshot;

            var profiles = registry.List().ToDictionary(p => p.Id, p => p);
            var snap = aggregator.Build(battles, profiles);
            _snapshot = snap;
            _signature = signature;
            return snap;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ComputeSignature(IReadOnlyList<BattleSummary> battles)
    {
        var entries = battles
            .Select(b =>
            {
                long ticks = 0;
                try { ticks = System.IO.File.GetLastWriteTimeUtc(b.FilePath).Ticks; }
                catch { }
                return $"{b.FilePath}|{ticks}";
            })
            .OrderBy(s => s, System.StringComparer.Ordinal);
        return string.Join(";", entries);
    }
}
