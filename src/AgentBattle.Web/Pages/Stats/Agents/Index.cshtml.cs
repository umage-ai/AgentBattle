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
