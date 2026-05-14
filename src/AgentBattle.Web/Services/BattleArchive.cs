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
    bool IsComplete,
    int StartingStack,
    IReadOnlyList<SeatedAgent> SeatedAgents,
    IReadOnlyList<RankEntry> Ranking);

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
        await foreach (var line in ReadLinesSharedAsync(file, ct))
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
        await foreach (var line in ReadLinesSharedAsync(file, ct))
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

        int startingStack = 0;
        try
        {
            var cfg = JsonSerializer.Deserialize<BattleConfig>(started.ConfigSnapshot, BattleEventJsonOptions.Default);
            if (cfg != null) startingStack = cfg.StartingStack;
        }
        catch (JsonException)
        {
            // Older battles may have a free-form snapshot string. Leave starting stack at 0;
            // chip-share for those rows will read as "n/a" in the UI.
        }

        return new BattleSummary(
            BattleId: started.BattleId,
            FilePath: file,
            StartedAt: started.Ts,
            AgentDisplayNames: started.Agents.Select(a => a.DisplayName).ToArray(),
            WinnerAgentId: winner,
            IsComplete: ended != null,
            StartingStack: startingStack,
            SeatedAgents: started.Agents,
            Ranking: ended?.Ranking ?? []);
    }

    /// <summary>
    /// Reads a file line-by-line with maximally permissive sharing so we can read a JSONL
    /// while the BattleRunner is still writing to it. <c>File.ReadLinesAsync</c> opens with
    /// <c>FileShare.Read | FileShare.Delete</c>, which conflicts with the writer's
    /// <c>FileAccess.Write</c> grant and throws <c>IOException</c>.
    /// </summary>
    private static async System.Collections.Generic.IAsyncEnumerable<string> ReadLinesSharedAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct)
    {
        await using var fs = new System.IO.FileStream(
            path,
            System.IO.FileMode.Open,
            System.IO.FileAccess.Read,
            System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
        using var reader = new System.IO.StreamReader(fs);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
            yield return line;
    }
}
