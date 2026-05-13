using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages;

public class IndexModel(BattleArchive archive) : PageModel
{
    public IReadOnlyList<BattleSummary> Battles { get; private set; } = [];
    public async System.Threading.Tasks.Task OnGetAsync() => Battles = await archive.ListBattlesAsync();
}
