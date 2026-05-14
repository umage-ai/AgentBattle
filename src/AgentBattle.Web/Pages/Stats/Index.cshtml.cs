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
