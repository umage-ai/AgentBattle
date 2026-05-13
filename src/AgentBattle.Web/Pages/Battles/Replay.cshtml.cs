using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Battles;

public class ReplayModel(BattleArchive archive) : PageModel
{
    public string BattleId { get; private set; } = "";

    public void OnGet(string id) => BattleId = id;

    public async System.Threading.Tasks.Task<IActionResult> OnGetEventsAsync(string id)
    {
        var events = await archive.LoadEventsAsync(id, HttpContext.RequestAborted);
        var json = System.Text.Json.JsonSerializer.Serialize(events, AgentBattle.Domain.Json.BattleEventJsonOptions.Default);
        return Content(json, "application/json");
    }
}
