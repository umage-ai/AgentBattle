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
