using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;
using AgentBattle.Domain.Battles;

namespace AgentBattle.Web.Pages.Agents;

public class IndexModel(AgentRegistry registry) : PageModel
{
    public IReadOnlyList<AgentProfile> Agents { get; private set; } = [];
    public void OnGet() => Agents = registry.List();
}
