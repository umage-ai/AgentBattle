using System.Text.Json;
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Json;

namespace AgentBattle.Web.Services;

public sealed record BattleSummary(
    string BattleId,
    string FilePath,
    System.DateTimeOffset StartedAt,
    IReadOnlyList<string> AgentDisplayNames,
    string? WinnerAgentId,
    bool IsComplete);

public sealed class BattleArchive(string battlesDir)
{
    public async System.Threading.Tasks.Task<IReadOnlyList<BattleSummary>> ListBattlesAsync(System.Threading.CancellationToken ct = default)
    {
        if (!System.IO.Directory.Exists(battlesDir)) return [];
        var summaries = new List<BattleSummary>();
        foreach (var file in System.IO.Directory.EnumerateFiles(battlesDir, "*.jsonl"))
        {
            var summary = await SummarizeAsync(file, ct);
            if (summary != null) summaries.Add(summary);
        }
        return summaries.OrderByDescending(s => s.StartedAt).ToArray();
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<BattleEvent>> LoadEventsAsync(string battleId, System.Threading.CancellationToken ct = default)
    {
        if (!System.IO.Directory.Exists(battlesDir)) return [];
        var file = System.IO.Directory.EnumerateFiles(battlesDir, "*.jsonl")
            .FirstOrDefault(p => System.IO.Path.GetFileName(p).Contains(battleId, System.StringComparison.OrdinalIgnoreCase));
        if (file == null) return [];
        var events = new List<BattleEvent>();
        await foreach (var line in System.IO.File.ReadLinesAsync(file, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var e = JsonSerializer.Deserialize<BattleEvent>(line, BattleEventJsonOptions.Default);
            if (e != null) events.Add(e);
        }
        return events;
    }

    private static async System.Threading.Tasks.Task<BattleSummary?> SummarizeAsync(string file, System.Threading.CancellationToken ct)
    {
        BattleEvent.BattleStarted? started = null;
        BattleEvent.BattleEnded? ended = null;
        await foreach (var line in System.IO.File.ReadLinesAsync(file, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<BattleEvent>(line, BattleEventJsonOptions.Default);
                switch (e)
                {
                    case BattleEvent.BattleStarted s: started = s; break;
                    case BattleEvent.BattleEnded x: ended = x; break;
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines — don't let one bad event poison the whole listing.
            }
        }
        if (started == null) return null;
        string? winner = null;
        if (ended != null && ended.Ranking.Count > 0)
            winner = ended.Ranking.OrderByDescending(r => r.Chips).First().AgentId;
        return new BattleSummary(
            BattleId: started.BattleId,
            FilePath: file,
            StartedAt: started.Ts,
            AgentDisplayNames: started.Agents.Select(a => a.DisplayName).ToArray(),
            WinnerAgentId: winner,
            IsComplete: ended != null);
    }
}
