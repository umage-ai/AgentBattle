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
