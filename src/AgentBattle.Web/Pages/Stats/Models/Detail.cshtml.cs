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

        var vsIdx = slug.LastIndexOf("-vs-", System.StringComparison.Ordinal);
        if (vsIdx > 0 && vsIdx + 4 < slug.Length)
        {
            var leftSlug = slug[..vsIdx];
            var rightSlug = slug[(vsIdx + 4)..];
            var leftKnown = snap.Models.Any(m => m.Slug == leftSlug);
            var rightKnown = snap.Models.Any(m => m.Slug == rightSlug);
            if (leftKnown && rightKnown)
            {
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
