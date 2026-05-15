using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentBattle.Web.Services;

public sealed record BattleSuggestion(
    string Id,
    System.DateTimeOffset CreatedAt,
    string SuggestedBy,
    string Game,
    IReadOnlyList<string> Agents,
    string Note);

/// <summary>
/// Persists visitor-submitted matchup suggestions to a single JSON file.
/// Append-only; readers see a snapshot. Concurrency is handled with a coarse lock —
/// this is a low-volume side feature, not a hot path.
/// </summary>
public sealed class SuggestionStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SuggestionStore(string path)
    {
        _path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
    }

    public IReadOnlyList<BattleSuggestion> List()
    {
        lock (_gate)
        {
            if (!System.IO.File.Exists(_path)) return [];
            try
            {
                var json = System.IO.File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return [];
                var items = JsonSerializer.Deserialize<List<BattleSuggestion>>(json, _opts) ?? [];
                return items.OrderByDescending(s => s.CreatedAt).ToArray();
            }
            catch (JsonException) { return []; }
        }
    }

    public BattleSuggestion Add(string suggestedBy, string game, IReadOnlyList<string> agents, string note)
    {
        var clean = new BattleSuggestion(
            Id: System.Guid.NewGuid().ToString("N")[..8],
            CreatedAt: System.DateTimeOffset.UtcNow,
            SuggestedBy: Trim(suggestedBy, 60),
            Game: Trim(game, 32),
            Agents: agents.Select(a => Trim(a, 80)).Where(a => a.Length > 0).Take(8).ToArray(),
            Note: Trim(note, 500));

        lock (_gate)
        {
            var existing = new List<BattleSuggestion>();
            if (System.IO.File.Exists(_path))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(_path);
                    if (!string.IsNullOrWhiteSpace(json))
                        existing = JsonSerializer.Deserialize<List<BattleSuggestion>>(json, _opts) ?? [];
                }
                catch (JsonException) { /* start fresh */ }
            }
            existing.Add(clean);
            // Cap at 500 to keep the file from growing forever.
            if (existing.Count > 500) existing = existing.OrderByDescending(s => s.CreatedAt).Take(500).ToList();
            System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(existing, _opts));
        }
        return clean;
    }

    private static string Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }
}
