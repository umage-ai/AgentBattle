# Stats & Drilldowns — Design

**Date:** 2026-05-14
**Status:** Draft for approval
**Author:** Allan + Claude

## Goal

Add a Stats section to the AgentBattle site so visitors can see how each LLM model — and each named agent profile — performs across the recorded poker battles, drill into individual matchups, and jump straight to the replay of any battle. The pages must be indexable for organic search on phrases like `gpt-4o-mini vs claude-haiku-4.5` so AgentBattle surfaces when someone googles "model A vs model B".

## Non-Goals

- No per-hand poker analytics (VPIP, PFR, aggression factor). Drilldown ends at the existing replay page.
- No charts or graphs. Tables only.
- No persistent stats database. We recompute from the JSONL battle archive on each request, cached by file mtime set.
- No date-range filtering. "Recent battles" lists provide enough recency signal.
- No backwards-compatible URL shims for stats pages — this is new surface.

## Two axes

Stats are exposed along two independent axes:

1. **Model** — the underlying LLM (`AgentProfile.Model`, e.g. `gpt-4o-mini`). Optimised for SEO ("model A vs model B" queries).
2. **Agent** — the named profile (`AgentProfile.DisplayName`, e.g. `Aggressive Annie`). Same model with two different personas counts as two different agents here.

Each axis has its own leaderboard, detail page, and head-to-head page. They are independently aggregated from the same underlying battle archive — no aliasing between them.

## Pages and routes

| Route | Page | SEO title |
|---|---|---|
| `/stats` | Combined landing — top 10 by win rate for each axis, with links | `Model & agent stats — AgentBattle` |
| `/stats/models` | Full model leaderboard, sortable | `LLM model leaderboard — AgentBattle` |
| `/stats/models/{model}` | One model: aggregate record, H2H table vs each opposing model, recent battles | `{model} — battle record \| AgentBattle` |
| `/stats/models/{a}-vs-{b}` | Model head-to-head: record, list of battles → replay | `{a} vs {b} — poker battles \| AgentBattle` |
| `/stats/agents` | Full agent leaderboard, sortable | `Agent leaderboard — AgentBattle` |
| `/stats/agents/{agent}` | One agent: aggregate record, H2H table vs each opposing agent, recent battles | `{agent} — battle record \| AgentBattle` |
| `/stats/agents/{a}-vs-{b}` | Agent head-to-head | `{a} vs {b} — agent battles \| AgentBattle` |
| `/sitemap.xml` | Enumerates `/`, both leaderboards, every encountered model, agent, and pairwise matchup on each axis | — |
| `/robots.txt` | Allow all, point at `/sitemap.xml` | — |

Header nav gets a new **Stats** link between Battles and Agents, pointing at `/stats`. Battle cards on the existing Battles index link each agent chip to that agent's detail page; the agent detail page links to the model page; the model page links back to each H2H matchup.

## Definitions

- **Win** = 1st place in `BattleEnded.Ranking` (highest chip stack when the battle ends). If two seats tie for 1st (rare), each gets `0.5` win in the totals.
- **Battle** counts for stats only if `BattleEnded` is present in the JSONL. In-progress battles are listed on the home page but excluded from aggregates.
- **Chip share** = `finalChips / startingStack`, averaged across battles. Surfaces "barely scraped 1st" vs "crushed everyone".
- **Head-to-head record** between A and B = computed only over battles where both A and B were seated. For each such battle, whichever finished with more chips at `BattleEnded` wins the H2H for that battle (a 3rd-place finish still beats a 5th-place finish on the same table). Exact chip ties split 0.5/0.5. This is independent of who won the battle overall: A and B can both have lost the battle to a third seat and still have a clean H2H result between them.

## Data flow

```
battles/*.jsonl ──► BattleArchive ───┐
                                     ├─► StatsAggregator ──► StatsSnapshot
agents/*.yaml ──► AgentRegistry ────┘                              │
                                                                   ▼
                                                /stats/* Razor pages, /sitemap.xml
```

`StatsAggregator` is a pure function:

```csharp
public sealed record StatsSnapshot(
    IReadOnlyList<ModelStats> Models,
    IReadOnlyList<AgentStats> Agents,
    IReadOnlyList<MatchupStats> ModelMatchups,
    IReadOnlyList<MatchupStats> AgentMatchups);

public StatsSnapshot Build(
    IReadOnlyList<BattleSummary> completedBattles,
    IReadOnlyDictionary<string, AgentProfile> agentsById);
```

`StatsAggregator` only consumes `BattleStarted` and `BattleEnded` events — no need to replay full event streams for aggregation. `BattleArchive` already extracts these into `BattleSummary`; we extend `BattleSummary` to expose the `Ranking` and the seated agent IDs so the aggregator gets everything it needs without re-reading files.

Model resolution: `agentsById[seatedAgent.Id].Model`. If the agent profile is missing (renamed/deleted since the battle ran), we fall back to `seatedAgent.DisplayName` and a `model=unknown` sentinel. The unknown bucket is excluded from leaderboards but still rendered on the agent's detail page so battles aren't silently lost.

Caching: `StatsAggregator` is wrapped by `StatsCache` keyed on the set of `(file path, mtime)` pairs in the battles directory. If any mtime changes or a file is added/removed, recompute. Battles archive is bounded (single-digit thousands at most for the MVP), so a full rescan is cheap.

## Slug rules

`ModelSlug.For(string)`:
1. Lowercase.
2. Replace any `[^a-z0-9]+` run with `-`.
3. Trim leading/trailing `-`.

`AgentSlug.For(string)` uses the same rules. A collision check in `StatsAggregator` logs a warning if two distinct source strings produce the same slug; the first one seen wins. (Not expected with current naming, but safer than silent merging.)

Matchup canonicalisation: `{a}-vs-{b}` always has `a < b` lexicographically. The page handler 301-redirects the reverse to the canonical form so we don't split SEO juice across two URLs.

## Routing under one slug parameter

`/stats/models/{slug}` covers both detail (`{model}`) and matchup (`{a}-vs-{b}`) URLs. Razor Pages only lets one `.cshtml` bind a given route shape, so one `Detail.cshtml` per axis handles both cases via internal dispatch in `OnGet`:

1. If the slug contains `-vs-`, split on the last occurrence. If both halves resolve to known model slugs → render matchup view. If the matchup is non-canonical (reverse order) → 301 to the canonical URL.
2. Otherwise, look up the slug as a single model. Match → render detail view.
3. If neither path matches → 404.

The view picks between detail and matchup layout using a `bool IsMatchup` flag set by the handler. Same dispatch shape for `Pages/Stats/Agents/Detail.cshtml`.

This also handles the edge case where a model name itself contains `-vs-` (e.g. a future LLM slugged `foo-vs-bar`): if neither half of the split is a known slug, fall through to treating the full string as a single model name.

## SEO essentials

Per page, via `_Layout.cshtml` sections:

- `<title>` and `<meta name="description">` populated from `ViewData`.
- `<link rel="canonical">` pointing at the canonical URL.
- OpenGraph: `og:title`, `og:description`, `og:type=website`, `og:url`.
- JSON-LD `BreadcrumbList` on detail and matchup pages (`AgentBattle → Stats → Models → {model}` etc.).

H1 on each H2H page is the literal phrase `{a} vs {b}` — same as the user's search query — followed by the battle count.

Slugs live in URLs; titles, H1s, meta descriptions, OpenGraph values, JSON-LD breadcrumbs, and on-page text always use the **original model or agent display string** (e.g. `claude-haiku-4.5`, not the URL slug `claude-haiku-4-5`). Pages cache the original-string lookup keyed on the slug so we never reverse-derive a name from a slug.

`/sitemap.xml` is a Razor page returning `application/xml`. It enumerates:

- `/`
- `/stats`, `/stats/models`, `/stats/agents`
- One URL per model and per agent
- One URL per canonical model matchup that has at least one completed battle
- One URL per canonical agent matchup that has at least one completed battle

`<lastmod>` for each entry uses the most recent battle's `StartedAt` for that entity. `<priority>` defaults to 0.5; leaderboards get 0.8.

## New files

```
src/AgentBattle.Web/
  Services/
    StatsAggregator.cs        # pure function, unit-tested
    StatsCache.cs             # mtime-keyed wrapper
    ModelSlug.cs              # slug helpers (handles model + agent)
  Pages/
    Stats/
      Index.cshtml(.cs)       # /stats
      Models/
        Index.cshtml(.cs)     # /stats/models
        Detail.cshtml(.cs)    # /stats/models/{slug} — handles single model AND a-vs-b
      Agents/
        Index.cshtml(.cs)     # /stats/agents
        Detail.cshtml(.cs)    # /stats/agents/{slug} — handles single agent AND a-vs-b
    Sitemap.cshtml(.cs)       # /sitemap.xml
    Robots.cshtml(.cs)        # /robots.txt
tests/AgentBattle.Web.Tests/
  StatsAggregatorTests.cs
  ModelSlugTests.cs
  StatsPagesSmokeTests.cs     # 200/404/redirect assertions
```

## Modifications to existing files

- `Services/BattleArchive.cs` — `BattleSummary` gains `Ranking` (the full `IReadOnlyList<RankEntry>`) and `SeatedAgents` (list of `(seat, id, displayName)`). `SummarizeAsync` already reads both events; just stash the data.
- `Pages/Shared/_Layout.cshtml` — add **Stats** nav entry; add `@RenderSection("Head", required: false)` so detail pages can inject meta/JSON-LD; add OpenGraph + canonical defaults.
- `Pages/Index.cshtml` — agent chips on each battle card link to `/stats/agents/{slug}` (the chip text stays the display name).
- `Program.cs` — register `StatsCache` as a singleton; `StatsAggregator` is stateless and can be `AddSingleton` too.
- `wwwroot/css/site.css` — small additions for stats tables and the new H2H matchup blocks. Reuse existing `battle-card`/`agent-chip` styles where possible.

## Testing

- `StatsAggregatorTests`: single battle, multi-battle aggregation, tied 1st place produces fractional wins, missing agent profile falls back to `unknown` model bucket, model that appears in two seats of the same battle (split agents same LLM).
- `ModelSlugTests`: lowercases, replaces punctuation, idempotent, collision detection.
- `StatsPagesSmokeTests` (using `WebApplicationFactory<Program>`):
  - 200 on `/stats`, `/stats/models`, `/stats/agents`, every model detail, every agent detail, every canonical matchup.
  - 404 on unknown slugs (both single-slug and matchup shapes).
  - 301 redirect from `b-vs-a` → `a-vs-b` when `a < b`.
  - Slug with `-vs-` whose halves aren't known models is treated as a single model lookup (no false matchup detection).
  - `/sitemap.xml` returns `application/xml` and contains one `<url>` per enumerated entity.
- `/robots.txt` smoke test: serves `text/plain` with `Sitemap:` line.

## Open items resolved during brainstorming

- **Axis key:** both (model + agent), separate sections, separate aggregates, separate sitemap entries.
- **Scope:** all three pages per axis + sitemap + robots — full slice.
- **Ties:** split as fractional wins.
- **Missing profile fallback:** `unknown` bucket on model axis, kept on agent axis under display name.
- **Reverse matchup URL:** 301 to canonical.

## Risk notes

- **JSONL filename heuristic in `BattleArchive.LoadEventsAsync`** uses `Contains(battleId, OrdinalIgnoreCase)`. If a stats page eventually wants full events (it doesn't for the MVP), the same lookup applies — flag for future-proofing only.
- **`AgentRegistry` is in-memory and read at startup.** If an agent file is added after the web server is running, those battles will fall into the `unknown` model bucket until restart. Acceptable for MVP; document in CLAUDE.md if it becomes painful.
