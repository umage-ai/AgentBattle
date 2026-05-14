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
