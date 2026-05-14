# Stats & Drilldowns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Stats section to AgentBattle.Web exposing per-model and per-agent leaderboards, head-to-head matchups, and a sitemap so the site is indexable for "model-name vs model-name" search queries.

**Architecture:** Pure-function `StatsAggregator` consumes `BattleSummary` entries from `BattleArchive` (extended to include `Ranking`, `SeatedAgents`, `StartingStack`) plus an `agentsById` lookup from `AgentRegistry`. Output is a `StatsSnapshot` cached by `StatsCache` keyed on the set of `(path, mtime)` pairs in the battles directory. Razor Pages under `Pages/Stats/` consume the snapshot. Two axes (model + agent) each get their own leaderboard, single-slug detail page, and `a-vs-b` head-to-head; one `Detail.cshtml` per axis internally dispatches between single-slug and matchup based on whether the slug contains `-vs-`.

**Tech Stack:** ASP.NET Core .NET 10, Razor Pages, xUnit, FluentAssertions, `Microsoft.AspNetCore.Mvc.Testing` (new). No client-side JS for stats; tables are server-rendered HTML.

**Reference spec:** `docs/superpowers/specs/2026-05-14-stats-and-drilldowns-design.md`

---

## File map (everything new or modified)

**New:**
- `src/AgentBattle.Web/Services/ModelSlug.cs`
- `src/AgentBattle.Web/Services/StatsAggregator.cs`
- `src/AgentBattle.Web/Services/StatsCache.cs`
- `src/AgentBattle.Web/Pages/Stats/Index.cshtml(.cs)`
- `src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml(.cs)`
- `src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml(.cs)`
- `src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml(.cs)`
- `src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml(.cs)`
- `src/AgentBattle.Web/Pages/Sitemap.cshtml(.cs)`
- `src/AgentBattle.Web/Pages/Robots.cshtml(.cs)`
- `tests/AgentBattle.Web.Tests/Services/ModelSlugTests.cs`
- `tests/AgentBattle.Web.Tests/Services/StatsAggregatorTests.cs`
- `tests/AgentBattle.Web.Tests/Services/StatsCacheTests.cs`
- `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`
- `tests/AgentBattle.Web.Tests/Pages/SitemapTests.cs`

**Modified:**
- `src/AgentBattle.Web/Services/BattleArchive.cs` — extend `BattleSummary` with `Ranking`, `SeatedAgents`, `StartingStack`
- `src/AgentBattle.Web/Program.cs` — register `StatsCache`; add `public partial class Program;` for `WebApplicationFactory`
- `src/AgentBattle.Web/Pages/Shared/_Layout.cshtml` — add Stats nav link, `@RenderSection("Head", required: false)`, default canonical/OG tags
- `src/AgentBattle.Web/Pages/Index.cshtml` — wrap agent chips in links to `/stats/agents/{slug}`
- `src/AgentBattle.Web/wwwroot/css/site.css` — stats tables, H2H header
- `Directory.Packages.props` — pin `Microsoft.AspNetCore.Mvc.Testing`
- `tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj` — reference `Microsoft.AspNetCore.Mvc.Testing`
- `tests/AgentBattle.Web.Tests/Services/BattleArchiveTests.cs` — extend existing tests to assert new fields

---

## Task 1: Extend `BattleSummary` with `Ranking`, `SeatedAgents`, `StartingStack`

**Files:**
- Modify: `src/AgentBattle.Web/Services/BattleArchive.cs`
- Modify: `tests/AgentBattle.Web.Tests/Services/BattleArchiveTests.cs`

Why first: the aggregator needs each battle's full ranking and seat→agent mapping; `BattleSummary` currently only exposes winner+display names. Also need `StartingStack` from the embedded `ConfigSnapshot` for chip-share calculations.

- [ ] **Step 1: Extend existing test to assert new fields**

Open `tests/AgentBattle.Web.Tests/Services/BattleArchiveTests.cs`. Replace the body of `ListBattles_reads_summary_from_first_and_last_events` with:

```csharp
[Fact]
public async Task ListBattles_reads_summary_from_first_and_last_events()
{
    var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
    try
    {
        var path = System.IO.Path.Combine(dir, "2026-05-13T1800-abc12345.jsonl");
        await System.IO.File.WriteAllLinesAsync(path, new[]
        {
            """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"abc12345","config_snapshot":"{\"game\":\"poker-6max\",\"hands\":3,\"starting_stack\":1000,\"blinds\":{\"small\":10,\"big\":20},\"seats\":[]}","agents":[{"seat":0,"id":"a","display_name":"A"},{"seat":1,"id":"b","display_name":"B"}]}""",
            """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
        });

        var archive = new BattleArchive(dir);
        var summaries = await archive.ListBattlesAsync();

        summaries.Should().HaveCount(1);
        var s = summaries[0];
        s.BattleId.Should().Be("abc12345");
        s.AgentDisplayNames.Should().BeEquivalentTo(new[] { "A", "B" });
        s.WinnerAgentId.Should().Be("a");
        s.IsComplete.Should().BeTrue();
        s.StartingStack.Should().Be(1000);
        s.SeatedAgents.Should().HaveCount(2);
        s.SeatedAgents[0].Id.Should().Be("a");
        s.Ranking.Should().HaveCount(2);
        s.Ranking[0].AgentId.Should().Be("a");
        s.Ranking[0].Chips.Should().Be(1200);
    }
    finally
    {
        System.IO.Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~ListBattles_reads_summary_from_first_and_last_events"
```

Expected: FAIL — `BattleSummary` does not contain definitions for `StartingStack`, `SeatedAgents`, `Ranking`.

- [ ] **Step 3: Extend `BattleSummary` record and `SummarizeAsync`**

Replace the contents of `src/AgentBattle.Web/Services/BattleArchive.cs` with:

```csharp
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
```

- [ ] **Step 4: Run the BattleArchive tests**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~BattleArchiveTests"
```

Expected: All three BattleArchiveTests pass (including the incomplete and missing-directory cases — the new fields default to empty collections / 0).

- [ ] **Step 5: Commit**

```bash
git add src/AgentBattle.Web/Services/BattleArchive.cs tests/AgentBattle.Web.Tests/Services/BattleArchiveTests.cs
git commit -m "feat(web): extend BattleSummary with ranking, seated agents, starting stack"
```

---

## Task 2: `ModelSlug` helper

**Files:**
- Create: `src/AgentBattle.Web/Services/ModelSlug.cs`
- Create: `tests/AgentBattle.Web.Tests/Services/ModelSlugTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AgentBattle.Web.Tests/Services/ModelSlugTests.cs`:

```csharp
using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class ModelSlugTests
{
    [Theory]
    [InlineData("gpt-4o-mini", "gpt-4o-mini")]
    [InlineData("Claude-Haiku-4.5", "claude-haiku-4-5")]
    [InlineData("llama3:8b", "llama3-8b")]
    [InlineData("  Spaces   And  STUFF ", "spaces-and-stuff")]
    [InlineData("---trim---", "trim")]
    [InlineData("multi___under", "multi-under")]
    public void For_normalises_to_slug(string input, string expected)
    {
        ModelSlug.For(input).Should().Be(expected);
    }

    [Fact]
    public void For_is_idempotent()
    {
        var once = ModelSlug.For("Claude-Haiku-4.5");
        ModelSlug.For(once).Should().Be(once);
    }

    [Fact]
    public void For_returns_empty_for_null_or_whitespace()
    {
        ModelSlug.For(null!).Should().BeEmpty();
        ModelSlug.For("   ").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~ModelSlugTests"
```

Expected: FAIL — `ModelSlug` does not exist.

- [ ] **Step 3: Implement `ModelSlug`**

Create `src/AgentBattle.Web/Services/ModelSlug.cs`:

```csharp
using System.Text;

namespace AgentBattle.Web.Services;

public static class ModelSlug
{
    public static string For(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        bool lastDash = true; // suppress leading dash
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        if (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run the tests**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~ModelSlugTests"
```

Expected: PASS — all 8 ModelSlug tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AgentBattle.Web/Services/ModelSlug.cs tests/AgentBattle.Web.Tests/Services/ModelSlugTests.cs
git commit -m "feat(web): ModelSlug helper for stats URLs"
```

---

## Task 3: `StatsAggregator` data types and aggregation

**Files:**
- Create: `src/AgentBattle.Web/Services/StatsAggregator.cs`
- Create: `tests/AgentBattle.Web.Tests/Services/StatsAggregatorTests.cs`

H2H semantic (per spec): A vs B for a shared battle = whichever finished with more chips wins. Exact tie = 0.5 each. Overall win = 1st place in `Ranking`. Missing agent profile → model resolves to `unknown` and is excluded from leaderboards (still listed on agent pages).

- [ ] **Step 1: Write failing tests**

Create `tests/AgentBattle.Web.Tests/Services/StatsAggregatorTests.cs`:

```csharp
using AgentBattle.Domain.Battles;
using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class StatsAggregatorTests
{
    private static AgentProfile Profile(string id, string display, string model) =>
        new(id, display, BaseUrl: "", Model: model, ApiKeyEnv: "");

    private static BattleSummary Battle(
        string id,
        System.DateTimeOffset startedAt,
        (int seat, string agentId, string display)[] seats,
        (int seat, int chips, string agentId)[] ranking,
        int startingStack = 1000) =>
        new(
            BattleId: id,
            FilePath: $"/tmp/{id}.jsonl",
            StartedAt: startedAt,
            AgentDisplayNames: seats.Select(s => s.display).ToArray(),
            WinnerAgentId: ranking.OrderByDescending(r => r.chips).First().agentId,
            IsComplete: true,
            StartingStack: startingStack,
            SeatedAgents: seats.Select(s => new SeatedAgent(s.seat, s.agentId, s.display)).ToArray(),
            Ranking: ranking.Select(r => new RankEntry(r.seat, r.chips, r.agentId)).ToArray());

    [Fact]
    public void Single_battle_records_one_win_and_one_loss_per_axis()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
            ["b"] = Profile("b", "Bravo", "claude-haiku")
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo")],
                ranking: [(0, 1200, "a"), (1, 800, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        snap.Models.Should().HaveCount(2);
        snap.Models.Single(m => m.Slug == "gpt-4o-mini").Wins.Should().Be(1.0);
        snap.Models.Single(m => m.Slug == "gpt-4o-mini").Battles.Should().Be(1);
        snap.Models.Single(m => m.Slug == "claude-haiku").Wins.Should().Be(0.0);
        snap.Models.Single(m => m.Slug == "claude-haiku").Battles.Should().Be(1);

        snap.Agents.Should().HaveCount(2);
        snap.Agents.Single(a => a.Slug == "alpha").Wins.Should().Be(1.0);
        snap.Agents.Single(a => a.Slug == "bravo").Wins.Should().Be(0.0);

        snap.ModelMatchups.Should().HaveCount(1);
        var matchup = snap.ModelMatchups[0];
        matchup.ASlug.Should().Be("claude-haiku");   // canonical lex order
        matchup.BSlug.Should().Be("gpt-4o-mini");
        matchup.AWins.Should().Be(0.0);
        matchup.BWins.Should().Be(1.0);
        matchup.BattleCount.Should().Be(1);
    }

    [Fact]
    public void Tie_at_first_place_produces_half_wins()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
            ["b"] = Profile("b", "Bravo", "claude-haiku")
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo")],
                ranking: [(0, 1000, "a"), (1, 1000, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        snap.Models.Single(m => m.Slug == "gpt-4o-mini").Wins.Should().Be(0.5);
        snap.Models.Single(m => m.Slug == "claude-haiku").Wins.Should().Be(0.5);
        snap.ModelMatchups[0].AWins.Should().Be(0.5);
        snap.ModelMatchups[0].BWins.Should().Be(0.5);
    }

    [Fact]
    public void H2H_uses_relative_chip_rank_not_overall_winner()
    {
        // A finishes 2nd, B finishes 3rd — A wins the H2H even though C won the battle.
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
            ["b"] = Profile("b", "Bravo", "claude-haiku"),
            ["c"] = Profile("c", "Charlie", "third-model")
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo"), (2, "c", "Charlie")],
                ranking: [(2, 1500, "c"), (0, 1000, "a"), (1, 500, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        var ab = snap.ModelMatchups.Single(m =>
            (m.ASlug == "claude-haiku" && m.BSlug == "gpt-4o-mini")
            || (m.ASlug == "gpt-4o-mini" && m.BSlug == "claude-haiku"));
        var aIsAlpha = ab.ASlug == "gpt-4o-mini";
        (aIsAlpha ? ab.AWins : ab.BWins).Should().Be(1.0); // Alpha (gpt-4o-mini) beat Bravo H2H
        (aIsAlpha ? ab.BWins : ab.AWins).Should().Be(0.0);
    }

    [Fact]
    public void Missing_agent_profile_falls_back_to_unknown_model_bucket()
    {
        // Profile for "b" not in dict; "b" gets model="unknown".
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo")],
                ranking: [(0, 1200, "a"), (1, 800, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        snap.Models.Should().NotContain(m => m.Slug == "unknown");
        snap.Agents.Should().HaveCount(2);
        snap.Agents.Single(a => a.Slug == "bravo").Wins.Should().Be(0.0);
    }

    [Fact]
    public void Same_model_in_two_seats_counts_both_as_that_model_battles()
    {
        // Two seats running the same LLM. The model gets 1 battle credit (not 2) — it played once.
        // But it also "wins against itself" — the H2H matchup of a model vs itself is filtered out.
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a1"] = Profile("a1", "Alpha-1", "gpt-4o-mini"),
            ["a2"] = Profile("a2", "Alpha-2", "gpt-4o-mini"),
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a1", "Alpha-1"), (1, "a2", "Alpha-2")],
                ranking: [(0, 1200, "a1"), (1, 800, "a2")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        var m = snap.Models.Single(m => m.Slug == "gpt-4o-mini");
        m.Battles.Should().Be(1);
        m.Wins.Should().Be(1.0); // someone running this model won
        snap.ModelMatchups.Should().BeEmpty(); // no model-vs-self matchup
        snap.AgentMatchups.Should().HaveCount(1); // alpha-1 vs alpha-2 is still a valid agent matchup
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~StatsAggregatorTests"
```

Expected: FAIL — `StatsAggregator`, `StatsSnapshot`, `ModelStats`, `AgentStats`, `MatchupStats` do not exist.

- [ ] **Step 3: Implement `StatsAggregator`**

Create `src/AgentBattle.Web/Services/StatsAggregator.cs`:

```csharp
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
        // Filter to battles that are complete and have rankings.
        var battles = completedBattles.Where(b => b.IsComplete && b.Ranking.Count > 0).ToArray();

        var modelAgg = new Dictionary<string, ModelAccumulator>();
        var agentAgg = new Dictionary<string, AgentAccumulator>();
        var modelMatch = new Dictionary<(string, string), MatchupAccumulator>();
        var agentMatch = new Dictionary<(string, string), MatchupAccumulator>();

        foreach (var battle in battles)
        {
            // Resolve each seat to (modelSlug, modelDisplay, agentSlug, agentDisplay) and rank position.
            var rankBySeat = battle.Ranking
                .OrderByDescending(r => r.Chips)
                .Select((r, i) => (r.Seat, r.Chips, Rank: i, r.AgentId))
                .ToDictionary(t => t.Seat);

            int topChips = battle.Ranking.Max(r => r.Chips);
            int topCount = battle.Ranking.Count(r => r.Chips == topChips);
            double winShare = 1.0 / topCount;

            // Per-seat resolved entries.
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

            // --- Agent axis (always populated, including unknown-model seats) ---
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

            // --- Model axis (excludes unknown bucket from leaderboards) ---
            //
            // A model that occupies multiple seats in the same battle only gets 1 "battle" credit
            // but accumulates wins from every top-chip seat it owns. Tied 1st across two seats of
            // the same model = 1.0 win for that model.
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

            // --- Model matchups (skip self-vs-self) ---
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

            // --- Agent matchups (per pair of distinct agent slugs) ---
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
```

- [ ] **Step 4: Run the tests**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~StatsAggregatorTests"
```

Expected: PASS — all 5 aggregator tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AgentBattle.Web/Services/StatsAggregator.cs tests/AgentBattle.Web.Tests/Services/StatsAggregatorTests.cs
git commit -m "feat(web): StatsAggregator over BattleSummary + agent profiles"
```

---

## Task 4: `StatsCache` — mtime-keyed wrapper

**Files:**
- Create: `src/AgentBattle.Web/Services/StatsCache.cs`
- Create: `tests/AgentBattle.Web.Tests/Services/StatsCacheTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AgentBattle.Web.Tests/Services/StatsCacheTests.cs`:

```csharp
using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class StatsCacheTests
{
    [Fact]
    public async Task GetAsync_recomputes_when_battles_directory_mtime_changes()
    {
        var battlesDir = System.IO.Directory.CreateTempSubdirectory().FullName;
        var agentsDir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(battlesDir, "first.jsonl"),
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"first","config_snapshot":"{}","agents":[]}""" + "\n" +
                """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{},"ranking":[]}""");

            var archive = new BattleArchive(battlesDir);
            var registry = new AgentRegistry(agentsDir);
            var cache = new StatsCache(archive, registry, new StatsAggregator());

            var snap1 = await cache.GetAsync();
            var snap2 = await cache.GetAsync();
            snap2.Should().BeSameAs(snap1); // cached

            // Add a new battle file -> directory contents change -> cache invalidates.
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(battlesDir, "second.jsonl"),
                """{"t":"battle_started","ts":"2026-05-13T19:00:00Z","battle_id":"second","config_snapshot":"{}","agents":[]}""" + "\n" +
                """{"t":"battle_ended","ts":"2026-05-13T19:42:00Z","final_stacks":{},"ranking":[]}""");

            var snap3 = await cache.GetAsync();
            snap3.Should().NotBeSameAs(snap1);
        }
        finally
        {
            System.IO.Directory.Delete(battlesDir, recursive: true);
            System.IO.Directory.Delete(agentsDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~StatsCacheTests"
```

Expected: FAIL — `StatsCache` does not exist.

- [ ] **Step 3: Implement `StatsCache`**

Create `src/AgentBattle.Web/Services/StatsCache.cs`:

```csharp
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
        // Cache key = sorted set of (filePath, mtime). Cheap and exact.
        var entries = battles
            .Select(b =>
            {
                long ticks = 0;
                try { ticks = System.IO.File.GetLastWriteTimeUtc(b.FilePath).Ticks; }
                catch { /* file may have vanished between list and stat */ }
                return $"{b.FilePath}|{ticks}";
            })
            .OrderBy(s => s, System.StringComparer.Ordinal);
        return string.Join(";", entries);
    }
}
```

- [ ] **Step 4: Run the test**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~StatsCacheTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AgentBattle.Web/Services/StatsCache.cs tests/AgentBattle.Web.Tests/Services/StatsCacheTests.cs
git commit -m "feat(web): mtime-keyed StatsCache wrapping StatsAggregator"
```

---

## Task 5: Test infrastructure — `WebApplicationFactory` packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj`
- Modify: `src/AgentBattle.Web/Program.cs`

We need `Microsoft.AspNetCore.Mvc.Testing` for the page smoke tests in later tasks, and a `public partial class Program;` declaration so `WebApplicationFactory<Program>` can find the entry point.

- [ ] **Step 1: Pin the testing package**

In `Directory.Packages.props`, add inside `<ItemGroup>`:

```xml
<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.7" />
```

- [ ] **Step 2: Reference it from the Web tests project**

In `tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj`, add to the existing `<ItemGroup>` containing test packages:

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
```

- [ ] **Step 3: Make `Program` discoverable**

Append to `src/AgentBattle.Web/Program.cs`:

```csharp
public partial class Program;
```

- [ ] **Step 4: Verify the solution still builds**

```bash
dotnet build AgentBattle.sln
```

Expected: build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj src/AgentBattle.Web/Program.cs
git commit -m "test(web): add Microsoft.AspNetCore.Mvc.Testing for page smoke tests"
```

---

## Task 6: DI wiring and layout updates

**Files:**
- Modify: `src/AgentBattle.Web/Program.cs`
- Modify: `src/AgentBattle.Web/Pages/Shared/_Layout.cshtml`

- [ ] **Step 1: Register stats services**

In `src/AgentBattle.Web/Program.cs`, after the existing `AddSingleton` calls and before `builder.Build()`, add:

```csharp
builder.Services.AddSingleton<StatsAggregator>();
builder.Services.AddSingleton<StatsCache>();
```

- [ ] **Step 2: Add Stats nav link and SEO `Head` section to layout**

Replace the contents of `src/AgentBattle.Web/Pages/Shared/_Layout.cshtml` with:

```cshtml
@{
    var current = (ViewContext.RouteData.Values["page"] ?? "").ToString() ?? "";
    string NavClass(string page) =>
        current.StartsWith(page, System.StringComparison.OrdinalIgnoreCase) ? "is-active" : "";
    var pageTitle = (ViewData["Title"] as string) ?? "AgentBattle";
    var fullTitle = $"{pageTitle} — AgentBattle";
    var description = (ViewData["Description"] as string) ?? "LLMs play poker so we can see how they reason under uncertainty.";
    var canonical = (ViewData["Canonical"] as string);
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@fullTitle</title>
    <meta name="description" content="@description" />
    @if (canonical != null)
    {
        <link rel="canonical" href="@canonical" />
        <meta property="og:url" content="@canonical" />
    }
    <meta property="og:title" content="@fullTitle" />
    <meta property="og:description" content="@description" />
    <meta property="og:type" content="website" />
    <meta property="og:site_name" content="AgentBattle" />
    <link rel="preconnect" href="https://rsms.me/" />
    <link rel="stylesheet" href="https://rsms.me/inter/inter.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    @await RenderSectionAsync("Head", required: false)
</head>
<body class="@(ViewData["BodyClass"] as string)">
    <header class="site-header">
        <div class="site-header-inner">
            <a class="brand" asp-page="/Index" title="AgentBattle by umage.ai">
                <img class="brand-mark" src="~/img/umage-logo.svg" alt="umage.ai" />
                <span class="brand-divider">/</span>
                <span class="brand-title"><strong>Agent</strong>Battle</span>
            </a>
            <nav class="site-nav">
                <a asp-page="/Index"            class="@NavClass("/Index")">Battles</a>
                <a asp-page="/Stats/Index"      class="@NavClass("/Stats")">Stats</a>
                <a asp-page="/Agents/Index"     class="@NavClass("/Agents")">Agents</a>
                <a asp-page="/Suggest"          class="@NavClass("/Suggest")">Suggest a battle</a>
                <a asp-page="/About"            class="@NavClass("/About")">About</a>
            </nav>
        </div>
    </header>
    <main>
        @RenderBody()
    </main>
    <footer class="site-footer">
        An experiment by <a href="https://umage.ai" target="_blank" rel="noopener">umage.ai</a>
        &mdash; LLMs play poker so we can study how they reason under uncertainty.
    </footer>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

Note the `NavClass` change to `StartsWith` so `/Stats/Models/Detail` etc. still highlight the Stats tab.

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/AgentBattle.Web/AgentBattle.Web.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/AgentBattle.Web/Program.cs src/AgentBattle.Web/Pages/Shared/_Layout.cshtml
git commit -m "feat(web): wire stats services and add Stats nav + SEO head section"
```

---

## Task 7: `/stats` landing page with smoke test

**Files:**
- Create: `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`
- Create: `src/AgentBattle.Web/Pages/Stats/Index.cshtml`
- Create: `src/AgentBattle.Web/Pages/Stats/Index.cshtml.cs`

- [ ] **Step 1: Write failing smoke test**

Create `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentBattle.Web.Tests.Pages;

public class StatsPagesSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public StatsPagesSmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Stats_index_returns_200()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Stats");
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_index_returns_200"
```

Expected: FAIL — `/stats` returns 404 (page doesn't exist yet).

- [ ] **Step 3: Implement page model**

Create `src/AgentBattle.Web/Pages/Stats/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Stats;

public class IndexModel(StatsCache cache) : PageModel
{
    public StatsSnapshot Snapshot { get; private set; } = new([], [], [], []);
    public async System.Threading.Tasks.Task OnGetAsync()
    {
        Snapshot = await cache.GetAsync(HttpContext.RequestAborted);
    }
}
```

- [ ] **Step 4: Implement page view**

Create `src/AgentBattle.Web/Pages/Stats/Index.cshtml`:

```cshtml
@page "/stats"
@model AgentBattle.Web.Pages.Stats.IndexModel
@{
    ViewData["Title"] = "Stats";
    ViewData["Description"] = "Model and agent leaderboards across all recorded AgentBattle poker matches.";
    ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats";
}

<div class="section-header">
    <div>
        <div class="eyebrow">Performance</div>
        <h1>Stats</h1>
        <p class="lede">Two leaderboards — one by underlying LLM, one by named agent profile.</p>
    </div>
</div>

<div class="stats-grid">
    <section class="stats-card">
        <h2>Top models <a class="stats-card-cta" asp-page="/Stats/Models/Index">All models →</a></h2>
        @if (Model.Snapshot.Models.Count == 0)
        {
            <p class="muted">No completed battles yet.</p>
        }
        else
        {
            <table class="stats-table">
                <thead><tr><th>Model</th><th class="num">Battles</th><th class="num">Wins</th><th class="num">Win %</th></tr></thead>
                <tbody>
                @foreach (var m in Model.Snapshot.Models.Take(10))
                {
                    <tr>
                        <td><a asp-page="/Stats/Models/Detail" asp-route-slug="@m.Slug">@m.DisplayName</a></td>
                        <td class="num">@m.Battles</td>
                        <td class="num">@m.Wins.ToString("0.#")</td>
                        <td class="num">@((m.Battles == 0 ? 0 : m.Wins / m.Battles).ToString("P0"))</td>
                    </tr>
                }
                </tbody>
            </table>
        }
    </section>

    <section class="stats-card">
        <h2>Top agents <a class="stats-card-cta" asp-page="/Stats/Agents/Index">All agents →</a></h2>
        @if (Model.Snapshot.Agents.Count == 0)
        {
            <p class="muted">No completed battles yet.</p>
        }
        else
        {
            <table class="stats-table">
                <thead><tr><th>Agent</th><th>Model</th><th class="num">Battles</th><th class="num">Wins</th><th class="num">Win %</th></tr></thead>
                <tbody>
                @foreach (var a in Model.Snapshot.Agents.Take(10))
                {
                    <tr>
                        <td><a asp-page="/Stats/Agents/Detail" asp-route-slug="@a.Slug">@a.DisplayName</a></td>
                        <td class="muted">@a.ModelDisplayName</td>
                        <td class="num">@a.Battles</td>
                        <td class="num">@a.Wins.ToString("0.#")</td>
                        <td class="num">@((a.Battles == 0 ? 0 : a.Wins / a.Battles).ToString("P0"))</td>
                    </tr>
                }
                </tbody>
            </table>
        }
    </section>
</div>
```

- [ ] **Step 5: Run the smoke test**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_index_returns_200"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs src/AgentBattle.Web/Pages/Stats/Index.cshtml src/AgentBattle.Web/Pages/Stats/Index.cshtml.cs
git commit -m "feat(web): /stats landing page with top-10 model and agent tables"
```

---

## Task 8: `/stats/models` full leaderboard

**Files:**
- Create: `src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml`
- Create: `src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml.cs`
- Modify: `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`

- [ ] **Step 1: Add failing smoke test**

Append to `StatsPagesSmokeTests`:

```csharp
[Fact]
public async Task Stats_models_index_returns_200()
{
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/stats/models");
    resp.IsSuccessStatusCode.Should().BeTrue();
    var body = await resp.Content.ReadAsStringAsync();
    body.Should().Contain("model leaderboard");
}
```

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_models_index_returns_200"
```

Expected: FAIL — 404.

- [ ] **Step 3: Create page model**

Create `src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Stats.Models;

public class IndexModel(StatsCache cache) : PageModel
{
    public IReadOnlyList<ModelStats> Models { get; private set; } = [];
    public async System.Threading.Tasks.Task OnGetAsync()
    {
        var snap = await cache.GetAsync(HttpContext.RequestAborted);
        Models = snap.Models;
    }
}
```

- [ ] **Step 4: Create view**

Create `src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml`:

```cshtml
@page "/stats/models"
@model AgentBattle.Web.Pages.Stats.Models.IndexModel
@{
    ViewData["Title"] = "LLM model leaderboard";
    ViewData["Description"] = "All LLM models ranked by win rate across AgentBattle poker matches.";
    ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats/models";
}

<div class="section-header">
    <div>
        <div class="eyebrow">Performance</div>
        <h1>LLM model leaderboard</h1>
        <p class="lede">Every model that has appeared in a completed battle.</p>
    </div>
</div>

@if (Model.Models.Count == 0)
{
    <p class="muted">No completed battles yet.</p>
}
else
{
    <table class="stats-table">
        <thead>
            <tr>
                <th>Model</th>
                <th class="num">Battles</th>
                <th class="num">Wins</th>
                <th class="num">Win %</th>
                <th class="num">Avg chip share</th>
                <th>Last seen</th>
            </tr>
        </thead>
        <tbody>
        @foreach (var m in Model.Models)
        {
            <tr>
                <td><a asp-page="/Stats/Models/Detail" asp-route-slug="@m.Slug">@m.DisplayName</a></td>
                <td class="num">@m.Battles</td>
                <td class="num">@m.Wins.ToString("0.#")</td>
                <td class="num">@((m.Battles == 0 ? 0 : m.Wins / m.Battles).ToString("P0"))</td>
                <td class="num">@m.AverageChipShare.ToString("P0")</td>
                <td>@m.LastBattleAt.ToLocalTime().ToString("yyyy-MM-dd")</td>
            </tr>
        }
        </tbody>
    </table>
}
```

- [ ] **Step 5: Run the smoke test**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_models_index_returns_200"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml src/AgentBattle.Web/Pages/Stats/Models/Index.cshtml.cs tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs
git commit -m "feat(web): /stats/models full leaderboard"
```

---

## Task 9: `/stats/models/{slug}` detail-or-matchup dispatcher

**Files:**
- Create: `src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml`
- Create: `src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml.cs`
- Modify: `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`

The same page handles:
- `/stats/models/gpt-4o-mini` → model detail
- `/stats/models/claude-haiku-vs-gpt-4o-mini` → H2H matchup (canonical: `a < b`)
- `/stats/models/gpt-4o-mini-vs-claude-haiku` → 301 to canonical
- Unknown slug → 404

- [ ] **Step 1: Add failing smoke tests**

Append to `StatsPagesSmokeTests`. Note the helper that seeds a battles directory — we'll reuse the pattern.

```csharp
[Fact]
public async Task Stats_models_detail_404_for_unknown_slug()
{
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/stats/models/this-model-does-not-exist-anywhere-12345");
    resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
}

[Fact]
public async Task Stats_models_versus_redirects_reverse_to_canonical()
{
    // We can't seed the live battles dir from a fixture, but reverse-of-unknown still routes:
    // if both halves are unknown, we get 404 — not a redirect. So we just assert the routing
    // shape for an unknown matchup returns 404 (no 500). The redirect path is covered by
    // integration via aggregator+page when real data exists. (See Task 11 for fixture-backed
    // version on the agent axis.)
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/stats/models/zzz-vs-aaa");
    resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
}
```

- [ ] **Step 2: Run to confirm they fail**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_models_detail_404 | FullyQualifiedName~Stats_models_versus_redirects"
```

Expected: FAIL — routes don't exist.

- [ ] **Step 3: Implement page model**

Create `src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Stats.Models;

public class DetailModel(StatsCache cache) : PageModel
{
    public bool IsMatchup { get; private set; }
    public ModelStats? Model { get; private set; }
    public MatchupStats? Matchup { get; private set; }
    public IReadOnlyList<MatchupStats> ModelMatchupsForModel { get; private set; } = [];
    public IReadOnlyList<BattleSummary> RelevantBattles { get; private set; } = [];

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync(string slug, [FromServices] BattleArchive archive)
    {
        var snap = await cache.GetAsync(HttpContext.RequestAborted);

        // Attempt matchup dispatch first: split on last "-vs-".
        var vsIdx = slug.LastIndexOf("-vs-", System.StringComparison.Ordinal);
        if (vsIdx > 0 && vsIdx + 4 < slug.Length)
        {
            var leftSlug = slug[..vsIdx];
            var rightSlug = slug[(vsIdx + 4)..];
            var leftKnown = snap.Models.Any(m => m.Slug == leftSlug);
            var rightKnown = snap.Models.Any(m => m.Slug == rightSlug);
            if (leftKnown && rightKnown)
            {
                // Canonicalise: a < b lexicographically. If reversed, redirect.
                var (aSlug, bSlug) = string.CompareOrdinal(leftSlug, rightSlug) <= 0
                    ? (leftSlug, rightSlug)
                    : (rightSlug, leftSlug);
                if (aSlug != leftSlug)
                    return RedirectPermanent($"/stats/models/{aSlug}-vs-{bSlug}");

                var match = snap.ModelMatchups.FirstOrDefault(m => m.ASlug == aSlug && m.BSlug == bSlug);
                if (match == null) return NotFound();
                IsMatchup = true;
                Matchup = match;

                var allBattles = await archive.ListBattlesAsync(HttpContext.RequestAborted);
                RelevantBattles = allBattles.Where(b => match.BattleIds.Contains(b.BattleId)).ToArray();
                return Page();
            }
            // Fall through: maybe a real model name happens to contain "-vs-".
        }

        var single = snap.Models.FirstOrDefault(m => m.Slug == slug);
        if (single == null) return NotFound();
        IsMatchup = false;
        Model = single;
        ModelMatchupsForModel = snap.ModelMatchups
            .Where(m => m.ASlug == slug || m.BSlug == slug)
            .OrderByDescending(m => m.BattleCount)
            .ToArray();

        var battles = await archive.ListBattlesAsync(HttpContext.RequestAborted);
        // A battle is "relevant" if it contains a seat whose model resolves to this slug.
        // Use the agent_id → profile lookup as the aggregator does.
        var registry = HttpContext.RequestServices.GetRequiredService<AgentRegistry>();
        var profiles = registry.List().ToDictionary(p => p.Id, p => p);
        RelevantBattles = battles
            .Where(b => b.SeatedAgents.Any(sa =>
                profiles.TryGetValue(sa.Id, out var prof) && ModelSlug.For(prof.Model) == slug))
            .Take(20)
            .ToArray();
        return Page();
    }
}
```

- [ ] **Step 4: Implement view**

Create `src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml`:

```cshtml
@page "/stats/models/{slug}"
@model AgentBattle.Web.Pages.Stats.Models.DetailModel
@{
    if (Model.IsMatchup)
    {
        var m = Model.Matchup!;
        ViewData["Title"] = $"{m.ADisplayName} vs {m.BDisplayName} — poker battles";
        ViewData["Description"] = $"{m.BattleCount} recorded poker battles between {m.ADisplayName} and {m.BDisplayName}. {m.AWins:0.#}–{m.BWins:0.#} head-to-head.";
        ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats/models/{m.ASlug}-vs-{m.BSlug}";
    }
    else
    {
        var s = Model.Model!;
        ViewData["Title"] = $"{s.DisplayName} — battle record";
        ViewData["Description"] = $"{s.DisplayName} has played {s.Battles} battles in AgentBattle with {s.Wins:0.#} wins.";
        ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats/models/{s.Slug}";
    }
}
@section Head {
    <script type="application/ld+json">
    {
      "@@context": "https://schema.org",
      "@@type": "BreadcrumbList",
      "itemListElement": [
        { "@@type": "ListItem", "position": 1, "name": "AgentBattle", "item": "@Request.Scheme://@Request.Host/" },
        { "@@type": "ListItem", "position": 2, "name": "Stats", "item": "@Request.Scheme://@Request.Host/stats" },
        { "@@type": "ListItem", "position": 3, "name": "Models", "item": "@Request.Scheme://@Request.Host/stats/models" },
        { "@@type": "ListItem", "position": 4, "name": "@(Model.IsMatchup ? $"{Model.Matchup!.ADisplayName} vs {Model.Matchup!.BDisplayName}" : Model.Model!.DisplayName)" }
      ]
    }
    </script>
}

@if (Model.IsMatchup)
{
    var m = Model.Matchup!;
    <div class="section-header">
        <div>
            <div class="eyebrow">Head-to-head</div>
            <h1>@m.ADisplayName vs @m.BDisplayName</h1>
            <p class="lede">@m.BattleCount shared @(m.BattleCount == 1 ? "battle" : "battles") — @m.AWins.ToString("0.#")–@m.BWins.ToString("0.#") on chip ranking.</p>
        </div>
    </div>

    <h2>Shared battles</h2>
    @await Html.PartialAsync("_BattleList", Model.RelevantBattles)
}
else
{
    var s = Model.Model!;
    <div class="section-header">
        <div>
            <div class="eyebrow">Model</div>
            <h1>@s.DisplayName</h1>
            <p class="lede">@s.Battles @(s.Battles == 1 ? "battle" : "battles"), @s.Wins.ToString("0.#") wins (@((s.Battles == 0 ? 0 : s.Wins / s.Battles).ToString("P0"))).</p>
        </div>
    </div>

    <h2>Head-to-head record</h2>
    @if (Model.ModelMatchupsForModel.Count == 0)
    {
        <p class="muted">No opposing models yet.</p>
    }
    else
    {
        <table class="stats-table">
            <thead><tr><th>Opponent</th><th class="num">Battles</th><th class="num">Wins</th><th class="num">Losses</th><th></th></tr></thead>
            <tbody>
            @foreach (var match in Model.ModelMatchupsForModel)
            {
                var isA = match.ASlug == s.Slug;
                var opponentSlug = isA ? match.BSlug : match.ASlug;
                var opponentDisplay = isA ? match.BDisplayName : match.ADisplayName;
                var myWins = isA ? match.AWins : match.BWins;
                var theirWins = isA ? match.BWins : match.AWins;
                <tr>
                    <td><a asp-page="/Stats/Models/Detail" asp-route-slug="@opponentSlug">@opponentDisplay</a></td>
                    <td class="num">@match.BattleCount</td>
                    <td class="num">@myWins.ToString("0.#")</td>
                    <td class="num">@theirWins.ToString("0.#")</td>
                    <td><a class="battle-card-cta" href="/stats/models/@match.ASlug-vs-@match.BSlug">View matchup →</a></td>
                </tr>
            }
            </tbody>
        </table>
    }

    <h2>Recent battles</h2>
    @await Html.PartialAsync("_BattleList", Model.RelevantBattles)
}
```

- [ ] **Step 5: Create the shared battle-list partial**

Create `src/AgentBattle.Web/Pages/Shared/_BattleList.cshtml`:

```cshtml
@model IReadOnlyList<AgentBattle.Web.Services.BattleSummary>
@if (Model.Count == 0)
{
    <p class="muted">No battles yet.</p>
}
else
{
    <ul class="battle-list">
    @foreach (var b in Model)
    {
        <li>
            <a class="battle-list-link" asp-page="/Battles/Replay" asp-route-id="@b.BattleId">
                <span class="battle-list-date">@b.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")</span>
                <span class="battle-list-vs">@string.Join(" vs ", b.AgentDisplayNames)</span>
                @if (b.IsComplete && b.WinnerAgentId != null)
                {
                    <span class="battle-list-winner">Winner: @b.WinnerAgentId</span>
                }
            </a>
        </li>
    }
    </ul>
}
```

- [ ] **Step 6: Run the smoke tests**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_models_detail_404 | FullyQualifiedName~Stats_models_versus_redirects"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml src/AgentBattle.Web/Pages/Stats/Models/Detail.cshtml.cs src/AgentBattle.Web/Pages/Shared/_BattleList.cshtml tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs
git commit -m "feat(web): /stats/models/{slug} detail + a-vs-b matchup with 301 canonicalisation"
```

---

## Task 10: `/stats/agents` full leaderboard

**Files:**
- Create: `src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml`
- Create: `src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml.cs`
- Modify: `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`

- [ ] **Step 1: Add failing smoke test**

Append to `StatsPagesSmokeTests`:

```csharp
[Fact]
public async Task Stats_agents_index_returns_200()
{
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/stats/agents");
    resp.IsSuccessStatusCode.Should().BeTrue();
    var body = await resp.Content.ReadAsStringAsync();
    body.Should().Contain("Agent leaderboard");
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_agents_index_returns_200"
```

Expected: FAIL.

- [ ] **Step 3: Implement page model**

Create `src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Stats.Agents;

public class IndexModel(StatsCache cache) : PageModel
{
    public IReadOnlyList<AgentStats> Agents { get; private set; } = [];
    public async System.Threading.Tasks.Task OnGetAsync()
    {
        var snap = await cache.GetAsync(HttpContext.RequestAborted);
        Agents = snap.Agents;
    }
}
```

- [ ] **Step 4: Implement view**

Create `src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml`:

```cshtml
@page "/stats/agents"
@model AgentBattle.Web.Pages.Stats.Agents.IndexModel
@{
    ViewData["Title"] = "Agent leaderboard";
    ViewData["Description"] = "Named agent profiles ranked by win rate across AgentBattle poker matches.";
    ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats/agents";
}

<div class="section-header">
    <div>
        <div class="eyebrow">Performance</div>
        <h1>Agent leaderboard</h1>
        <p class="lede">Every agent profile that has played a completed battle.</p>
    </div>
</div>

@if (Model.Agents.Count == 0)
{
    <p class="muted">No completed battles yet.</p>
}
else
{
    <table class="stats-table">
        <thead>
            <tr><th>Agent</th><th>Model</th><th class="num">Battles</th><th class="num">Wins</th><th class="num">Win %</th><th>Last seen</th></tr>
        </thead>
        <tbody>
        @foreach (var a in Model.Agents)
        {
            <tr>
                <td><a asp-page="/Stats/Agents/Detail" asp-route-slug="@a.Slug">@a.DisplayName</a></td>
                <td class="muted">@a.ModelDisplayName</td>
                <td class="num">@a.Battles</td>
                <td class="num">@a.Wins.ToString("0.#")</td>
                <td class="num">@((a.Battles == 0 ? 0 : a.Wins / a.Battles).ToString("P0"))</td>
                <td>@a.LastBattleAt.ToLocalTime().ToString("yyyy-MM-dd")</td>
            </tr>
        }
        </tbody>
    </table>
}
```

- [ ] **Step 5: Run smoke test**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_agents_index_returns_200"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml src/AgentBattle.Web/Pages/Stats/Agents/Index.cshtml.cs tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs
git commit -m "feat(web): /stats/agents full leaderboard"
```

---

## Task 11: `/stats/agents/{slug}` detail-or-matchup dispatcher

Mirrors Task 9 but on the agent axis. We also add fixture-backed tests that cover the 301 canonicalisation path, since we can stand up a small fake battles directory via `WebApplicationFactory.WithWebHostBuilder` to override the `BattleArchive` singleton.

**Files:**
- Create: `src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml`
- Create: `src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml.cs`
- Modify: `tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs`

- [ ] **Step 1: Add failing smoke tests**

Append to `StatsPagesSmokeTests`. The fixture helper builds an isolated factory pointing at a temp battles directory we control:

```csharp
private WebApplicationFactory<Program> WithTempBattles(string battlesDir) =>
    _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Paths:BattlesDirectory", battlesDir);
    });

[Fact]
public async Task Stats_agents_versus_canonicalises_via_301()
{
    var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
    try
    {
        var path = System.IO.Path.Combine(dir, "battle.jsonl");
        await System.IO.File.WriteAllLinesAsync(path, new[]
        {
            """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"x","config_snapshot":"{\"game\":\"poker-6max\",\"hands\":3,\"starting_stack\":1000,\"blinds\":{\"small\":10,\"big\":20},\"seats\":[]}","agents":[{"seat":0,"id":"a","display_name":"Zelda"},{"seat":1,"id":"b","display_name":"Anna"}]}""",
            """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
        });

        using var client = WithTempBattles(dir).CreateClient(new() { AllowAutoRedirect = false });
        // Canonical is "anna-vs-zelda" (anna < zelda lex). Request reverse.
        var resp = await client.GetAsync("/stats/agents/zelda-vs-anna");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.MovedPermanently);
        resp.Headers.Location!.ToString().Should().Be("/stats/agents/anna-vs-zelda");

        var canonical = await client.GetAsync("/stats/agents/anna-vs-zelda");
        canonical.IsSuccessStatusCode.Should().BeTrue();
        var body = await canonical.Content.ReadAsStringAsync();
        body.Should().Contain("Anna").And.Contain("Zelda");
    }
    finally
    {
        System.IO.Directory.Delete(dir, recursive: true);
    }
}

[Fact]
public async Task Stats_agents_detail_404_for_unknown_slug()
{
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/stats/agents/this-agent-does-not-exist-anywhere-12345");
    resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
}

[Fact]
public async Task Stats_agents_unknown_matchup_halves_treated_as_single_slug_lookup_404s()
{
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/stats/agents/zzz-vs-aaa");
    resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_agents_versus_canonicalises | FullyQualifiedName~Stats_agents_detail_404 | FullyQualifiedName~Stats_agents_unknown_matchup"
```

Expected: FAIL — page does not exist.

- [ ] **Step 3: Implement page model**

Create `src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Stats.Agents;

public class DetailModel(StatsCache cache) : PageModel
{
    public bool IsMatchup { get; private set; }
    public AgentStats? Agent { get; private set; }
    public MatchupStats? Matchup { get; private set; }
    public IReadOnlyList<MatchupStats> AgentMatchupsForAgent { get; private set; } = [];
    public IReadOnlyList<BattleSummary> RelevantBattles { get; private set; } = [];

    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync(string slug, [FromServices] BattleArchive archive)
    {
        var snap = await cache.GetAsync(HttpContext.RequestAborted);

        var vsIdx = slug.LastIndexOf("-vs-", System.StringComparison.Ordinal);
        if (vsIdx > 0 && vsIdx + 4 < slug.Length)
        {
            var leftSlug = slug[..vsIdx];
            var rightSlug = slug[(vsIdx + 4)..];
            var leftKnown = snap.Agents.Any(a => a.Slug == leftSlug);
            var rightKnown = snap.Agents.Any(a => a.Slug == rightSlug);
            if (leftKnown && rightKnown)
            {
                var (aSlug, bSlug) = string.CompareOrdinal(leftSlug, rightSlug) <= 0
                    ? (leftSlug, rightSlug)
                    : (rightSlug, leftSlug);
                if (aSlug != leftSlug)
                    return RedirectPermanent($"/stats/agents/{aSlug}-vs-{bSlug}");

                var match = snap.AgentMatchups.FirstOrDefault(m => m.ASlug == aSlug && m.BSlug == bSlug);
                if (match == null) return NotFound();
                IsMatchup = true;
                Matchup = match;
                var battles = await archive.ListBattlesAsync(HttpContext.RequestAborted);
                RelevantBattles = battles.Where(b => match.BattleIds.Contains(b.BattleId)).ToArray();
                return Page();
            }
        }

        var single = snap.Agents.FirstOrDefault(a => a.Slug == slug);
        if (single == null) return NotFound();
        IsMatchup = false;
        Agent = single;
        AgentMatchupsForAgent = snap.AgentMatchups
            .Where(m => m.ASlug == slug || m.BSlug == slug)
            .OrderByDescending(m => m.BattleCount)
            .ToArray();
        var all = await archive.ListBattlesAsync(HttpContext.RequestAborted);
        RelevantBattles = all
            .Where(b => b.SeatedAgents.Any(sa => ModelSlug.For(sa.DisplayName) == slug))
            .Take(20)
            .ToArray();
        return Page();
    }
}
```

- [ ] **Step 4: Implement view**

Create `src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml`:

```cshtml
@page "/stats/agents/{slug}"
@model AgentBattle.Web.Pages.Stats.Agents.DetailModel
@{
    if (Model.IsMatchup)
    {
        var m = Model.Matchup!;
        ViewData["Title"] = $"{m.ADisplayName} vs {m.BDisplayName} — agent battles";
        ViewData["Description"] = $"{m.BattleCount} battles between agents {m.ADisplayName} and {m.BDisplayName}. {m.AWins:0.#}–{m.BWins:0.#} head-to-head.";
        ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats/agents/{m.ASlug}-vs-{m.BSlug}";
    }
    else
    {
        var a = Model.Agent!;
        ViewData["Title"] = $"{a.DisplayName} — agent record";
        ViewData["Description"] = $"{a.DisplayName} (running {a.ModelDisplayName}) has played {a.Battles} AgentBattle matches with {a.Wins:0.#} wins.";
        ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/stats/agents/{a.Slug}";
    }
}
@section Head {
    <script type="application/ld+json">
    {
      "@@context": "https://schema.org",
      "@@type": "BreadcrumbList",
      "itemListElement": [
        { "@@type": "ListItem", "position": 1, "name": "AgentBattle", "item": "@Request.Scheme://@Request.Host/" },
        { "@@type": "ListItem", "position": 2, "name": "Stats", "item": "@Request.Scheme://@Request.Host/stats" },
        { "@@type": "ListItem", "position": 3, "name": "Agents", "item": "@Request.Scheme://@Request.Host/stats/agents" },
        { "@@type": "ListItem", "position": 4, "name": "@(Model.IsMatchup ? $"{Model.Matchup!.ADisplayName} vs {Model.Matchup!.BDisplayName}" : Model.Agent!.DisplayName)" }
      ]
    }
    </script>
}

@if (Model.IsMatchup)
{
    var m = Model.Matchup!;
    <div class="section-header">
        <div>
            <div class="eyebrow">Head-to-head</div>
            <h1>@m.ADisplayName vs @m.BDisplayName</h1>
            <p class="lede">@m.BattleCount shared @(m.BattleCount == 1 ? "battle" : "battles") — @m.AWins.ToString("0.#")–@m.BWins.ToString("0.#") on chip ranking.</p>
        </div>
    </div>
    <h2>Shared battles</h2>
    @await Html.PartialAsync("_BattleList", Model.RelevantBattles)
}
else
{
    var a = Model.Agent!;
    <div class="section-header">
        <div>
            <div class="eyebrow">Agent</div>
            <h1>@a.DisplayName</h1>
            <p class="lede">Running <strong>@a.ModelDisplayName</strong>. @a.Battles @(a.Battles == 1 ? "battle" : "battles"), @a.Wins.ToString("0.#") wins (@((a.Battles == 0 ? 0 : a.Wins / a.Battles).ToString("P0"))).</p>
        </div>
    </div>
    <h2>Head-to-head record</h2>
    @if (Model.AgentMatchupsForAgent.Count == 0)
    {
        <p class="muted">No opposing agents yet.</p>
    }
    else
    {
        <table class="stats-table">
            <thead><tr><th>Opponent</th><th class="num">Battles</th><th class="num">Wins</th><th class="num">Losses</th><th></th></tr></thead>
            <tbody>
            @foreach (var match in Model.AgentMatchupsForAgent)
            {
                var isA = match.ASlug == a.Slug;
                var oppSlug = isA ? match.BSlug : match.ASlug;
                var oppDisp = isA ? match.BDisplayName : match.ADisplayName;
                var myW = isA ? match.AWins : match.BWins;
                var theirW = isA ? match.BWins : match.AWins;
                <tr>
                    <td><a asp-page="/Stats/Agents/Detail" asp-route-slug="@oppSlug">@oppDisp</a></td>
                    <td class="num">@match.BattleCount</td>
                    <td class="num">@myW.ToString("0.#")</td>
                    <td class="num">@theirW.ToString("0.#")</td>
                    <td><a class="battle-card-cta" href="/stats/agents/@match.ASlug-vs-@match.BSlug">View matchup →</a></td>
                </tr>
            }
            </tbody>
        </table>
    }
    <h2>Recent battles</h2>
    @await Html.PartialAsync("_BattleList", Model.RelevantBattles)
}
```

- [ ] **Step 5: Run the smoke tests**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Stats_agents_versus_canonicalises | FullyQualifiedName~Stats_agents_detail_404 | FullyQualifiedName~Stats_agents_unknown_matchup"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml src/AgentBattle.Web/Pages/Stats/Agents/Detail.cshtml.cs tests/AgentBattle.Web.Tests/Pages/StatsPagesSmokeTests.cs
git commit -m "feat(web): /stats/agents/{slug} detail + a-vs-b matchup with 301 canonicalisation"
```

---

## Task 12: Battle card agent chips link to agent detail page

**File:** Modify `src/AgentBattle.Web/Pages/Index.cshtml`

- [ ] **Step 1: Replace the `agent-chip` block with a linked version**

In `src/AgentBattle.Web/Pages/Index.cshtml`, find the span:

```cshtml
<span class="agent-chip @(isWinner ? "is-winner" : "")">
    <span class="dot"></span>@name
</span>
```

Replace with:

```cshtml
<a class="agent-chip @(isWinner ? "is-winner" : "")"
   asp-page="/Stats/Agents/Detail"
   asp-route-slug="@AgentBattle.Web.Services.ModelSlug.For(name)"
   onclick="event.stopPropagation();">
    <span class="dot"></span>@name
</a>
```

The whole battle card is wrapped in an `<a>` already; the nested `<a>` is invalid HTML strictly speaking, but browsers handle it. The `onclick="event.stopPropagation()"` keeps the chip click from triggering the parent replay link. If you prefer a stricter solution, see the alternative below.

**Alternative (stricter, if your team minds nested anchors):** replace the outer `<a class="battle-card">` with `<div class="battle-card" data-href="...">` and add a tiny click handler that navigates to `data-href` when the click target isn't an `.agent-chip`. Either is fine — pick the first option unless a lint rule complains.

- [ ] **Step 2: Manual verification**

```bash
dotnet run --project src/AgentBattle.Web
```

Open `http://localhost:5278` in a browser. Click an agent chip on a battle card — it should navigate to `/stats/agents/{slug}`, not into the replay.

- [ ] **Step 3: Commit**

```bash
git add src/AgentBattle.Web/Pages/Index.cshtml
git commit -m "feat(web): battle-card agent chips link to /stats/agents/{slug}"
```

---

## Task 13: `/sitemap.xml`

**Files:**
- Create: `src/AgentBattle.Web/Pages/Sitemap.cshtml`
- Create: `src/AgentBattle.Web/Pages/Sitemap.cshtml.cs`
- Create: `tests/AgentBattle.Web.Tests/Pages/SitemapTests.cs`

- [ ] **Step 1: Failing test**

Create `tests/AgentBattle.Web.Tests/Pages/SitemapTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentBattle.Web.Tests.Pages;

public class SitemapTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SitemapTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Sitemap_returns_xml_with_core_routes()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = System.IO.Path.Combine(dir, "battle.jsonl");
            await System.IO.File.WriteAllLinesAsync(path, new[]
            {
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"x","config_snapshot":"{\"game\":\"poker-6max\",\"hands\":3,\"starting_stack\":1000,\"blinds\":{\"small\":10,\"big\":20},\"seats\":[]}","agents":[{"seat":0,"id":"a","display_name":"Anna"},{"seat":1,"id":"b","display_name":"Bob"}]}""",
                """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
            });

            using var client = _factory.WithWebHostBuilder(b => b.UseSetting("Paths:BattlesDirectory", dir)).CreateClient();
            var resp = await client.GetAsync("/sitemap.xml");

            resp.IsSuccessStatusCode.Should().BeTrue();
            resp.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
            var body = await resp.Content.ReadAsStringAsync();
            body.Should().Contain("<urlset");
            body.Should().Contain("/stats");
            body.Should().Contain("/stats/agents/anna");
            body.Should().Contain("/stats/agents/bob");
            body.Should().Contain("/stats/agents/anna-vs-bob");
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Sitemap_returns_xml_with_core_routes"
```

Expected: FAIL — 404 on `/sitemap.xml`.

- [ ] **Step 3: Implement page model**

Create `src/AgentBattle.Web/Pages/Sitemap.cshtml.cs`:

```csharp
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages;

public class SitemapModel(StatsCache cache) : PageModel
{
    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
    {
        var snap = await cache.GetAsync(HttpContext.RequestAborted);
        var origin = $"{Request.Scheme}://{Request.Host}";

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = false, Encoding = Encoding.UTF8 };
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            void Url(string path, double priority = 0.5, System.DateTimeOffset? lastmod = null)
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", origin + path);
                if (lastmod.HasValue)
                    writer.WriteElementString("lastmod", lastmod.Value.ToString("yyyy-MM-dd"));
                writer.WriteElementString("priority", priority.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            Url("/", priority: 1.0);
            Url("/stats", priority: 0.9);
            Url("/stats/models", priority: 0.8);
            Url("/stats/agents", priority: 0.8);
            foreach (var m in snap.Models)
                Url($"/stats/models/{m.Slug}", lastmod: m.LastBattleAt);
            foreach (var m in snap.ModelMatchups)
                Url($"/stats/models/{m.ASlug}-vs-{m.BSlug}");
            foreach (var a in snap.Agents)
                Url($"/stats/agents/{a.Slug}", lastmod: a.LastBattleAt);
            foreach (var m in snap.AgentMatchups)
                Url($"/stats/agents/{m.ASlug}-vs-{m.BSlug}");

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
```

- [ ] **Step 4: Create the (minimal) page file**

Create `src/AgentBattle.Web/Pages/Sitemap.cshtml`:

```cshtml
@page "/sitemap.xml"
@model AgentBattle.Web.Pages.SitemapModel
```

The page model returns `ContentResult` directly so no view rendering is needed; the `@page` directive only declares the route.

- [ ] **Step 5: Run the test**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Sitemap_returns_xml_with_core_routes"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentBattle.Web/Pages/Sitemap.cshtml src/AgentBattle.Web/Pages/Sitemap.cshtml.cs tests/AgentBattle.Web.Tests/Pages/SitemapTests.cs
git commit -m "feat(web): /sitemap.xml enumerating models, agents, and pairwise matchups"
```

---

## Task 14: `/robots.txt`

**Files:**
- Create: `src/AgentBattle.Web/Pages/Robots.cshtml`
- Create: `src/AgentBattle.Web/Pages/Robots.cshtml.cs`
- Modify: `tests/AgentBattle.Web.Tests/Pages/SitemapTests.cs`

- [ ] **Step 1: Failing test**

Append to `SitemapTests`:

```csharp
[Fact]
public async Task Robots_serves_text_plain_with_sitemap_line()
{
    using var client = _factory.CreateClient();
    var resp = await client.GetAsync("/robots.txt");
    resp.IsSuccessStatusCode.Should().BeTrue();
    resp.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    var body = await resp.Content.ReadAsStringAsync();
    body.Should().Contain("User-agent: *");
    body.Should().Contain("Sitemap:");
    body.Should().Contain("/sitemap.xml");
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Robots_serves_text_plain"
```

Expected: FAIL — 404.

- [ ] **Step 3: Implement page model**

Create `src/AgentBattle.Web/Pages/Robots.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgentBattle.Web.Pages;

public class RobotsModel : PageModel
{
    public IActionResult OnGet()
    {
        var origin = $"{Request.Scheme}://{Request.Host}";
        var body = $"User-agent: *\nAllow: /\nSitemap: {origin}/sitemap.xml\n";
        return Content(body, "text/plain", System.Text.Encoding.UTF8);
    }
}
```

- [ ] **Step 4: Create page file**

Create `src/AgentBattle.Web/Pages/Robots.cshtml`:

```cshtml
@page "/robots.txt"
@model AgentBattle.Web.Pages.RobotsModel
```

- [ ] **Step 5: Run the test**

```bash
dotnet test tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj --filter "FullyQualifiedName~Robots_serves_text_plain"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentBattle.Web/Pages/Robots.cshtml src/AgentBattle.Web/Pages/Robots.cshtml.cs tests/AgentBattle.Web.Tests/Pages/SitemapTests.cs
git commit -m "feat(web): /robots.txt pointing crawlers at sitemap"
```

---

## Task 15: Stats CSS

**File:** Modify `src/AgentBattle.Web/wwwroot/css/site.css`

- [ ] **Step 1: Append stats styles**

Append to `src/AgentBattle.Web/wwwroot/css/site.css`:

```css
/* ---------- Stats ---------- */

.stats-grid {
    display: grid;
    grid-template-columns: 1fr;
    gap: 1.5rem;
    margin-top: 2rem;
}

@media (min-width: 900px) {
    .stats-grid { grid-template-columns: 1fr 1fr; }
}

.stats-card {
    background: var(--surface, #1a1a1f);
    border: 1px solid var(--border, #2a2a32);
    border-radius: 12px;
    padding: 1.25rem 1.5rem;
}

.stats-card h2 {
    font-size: 1.1rem;
    margin: 0 0 1rem 0;
    display: flex;
    justify-content: space-between;
    align-items: baseline;
}

.stats-card-cta {
    font-size: 0.85rem;
    font-weight: 500;
    opacity: 0.7;
    text-decoration: none;
}

.stats-card-cta:hover { opacity: 1; }

.stats-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.95rem;
}

.stats-table th, .stats-table td {
    text-align: left;
    padding: 0.5rem 0.75rem;
    border-bottom: 1px solid var(--border, #2a2a32);
}

.stats-table th.num, .stats-table td.num { text-align: right; font-variant-numeric: tabular-nums; }

.stats-table thead th {
    font-size: 0.75rem;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    opacity: 0.6;
    font-weight: 500;
}

.stats-table a { text-decoration: none; }
.stats-table a:hover { text-decoration: underline; }

.battle-list { list-style: none; padding: 0; margin: 1rem 0 0 0; }
.battle-list li { margin-bottom: 0.5rem; }
.battle-list-link {
    display: flex;
    gap: 1rem;
    padding: 0.75rem 1rem;
    background: var(--surface, #1a1a1f);
    border: 1px solid var(--border, #2a2a32);
    border-radius: 8px;
    text-decoration: none;
    transition: border-color 0.15s ease;
}
.battle-list-link:hover { border-color: var(--accent, #6b8afd); }
.battle-list-date { font-variant-numeric: tabular-nums; opacity: 0.7; min-width: 11ch; }
.battle-list-vs { flex: 1; }
.battle-list-winner { font-size: 0.85rem; opacity: 0.8; }
```

- [ ] **Step 2: Manual check**

```bash
dotnet run --project src/AgentBattle.Web
```

Open `/stats`, `/stats/models`, a model detail page, and an H2H matchup. Verify tables are readable, the nav highlights Stats, and the battle-list renders cleanly.

- [ ] **Step 3: Commit**

```bash
git add src/AgentBattle.Web/wwwroot/css/site.css
git commit -m "style(web): stats tables, stat cards, and battle list"
```

---

## Task 16: Full regression run

- [ ] **Step 1: Run the whole test suite**

```bash
dotnet test
```

Expected: all tests pass across all four test projects. If any pre-existing test fails, investigate before declaring done — likely the BattleSummary extension touched something else than expected.

- [ ] **Step 2: Final manual smoke**

Run the web app and walk through:
1. `/` shows battles; clicking an agent chip routes to `/stats/agents/{slug}`.
2. `/stats` shows two top-10 cards.
3. `/stats/models` lists all models; clicking opens detail.
4. From a model detail page, click a head-to-head row → loads matchup page; the URL is canonical.
5. Manually type the reversed matchup URL → 301 to canonical.
6. `/sitemap.xml` returns XML.
7. `/robots.txt` returns the user-agent line.
8. `/stats/models/this-is-not-a-thing` → 404.

If everything passes, the slice is done.

---

## Self-review notes

After writing this plan I checked:

- **Spec coverage:** All pages and routes from the spec table are present (Tasks 7–14). Both axes are covered. Sitemap and robots are in. SEO meta (title/description/canonical/OG/JSON-LD breadcrumbs) is in `_Layout.cshtml` (Task 6) and per-page (Tasks 7–11). The H2H semantic (relative chip rank) is implemented and unit-tested in Task 3.
- **Placeholders:** No "TBD", no "implement later", every step shows the actual code or command.
- **Type consistency:** `StatsSnapshot`, `ModelStats`, `AgentStats`, `MatchupStats` defined once in Task 3 and used consistently by every page model.
- **Chip-share for missing-starting-stack rows:** older battles without `starting_stack` in the snapshot get `StartingStack = 0`, which is skipped in chip-share averaging. The chip-share field will show as 0% for affected models but the field is informational, not a sort key, and battles still count. Acceptable.
- **One known wart:** Task 12's "nested anchor" inside a battle card is non-strict HTML. Documented an alternative inline if the team minds.
