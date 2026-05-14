using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages;

public class SitemapModel(StatsCache cache) : PageModel
{
    public async System.Threading.Tasks.Task<IActionResult> OnGetAsync()
    {
        var snap = await cache.GetAsync(HttpContext.RequestAborted);
        var origin = $"{Request.Scheme}://{Request.Host}";

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = false, Encoding = Encoding.UTF8 };
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            void Url(string path, double priority = 0.5, System.DateTimeOffset? lastmod = null)
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", origin + path);
                if (lastmod.HasValue)
                    writer.WriteElementString("lastmod", lastmod.Value.ToString("yyyy-MM-dd"));
                writer.WriteElementString("priority", priority.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            Url("/", priority: 1.0);
            Url("/stats", priority: 0.9);
            Url("/stats/models", priority: 0.8);
            Url("/stats/agents", priority: 0.8);
            foreach (var m in snap.Models)
                Url($"/stats/models/{m.Slug}", lastmod: m.LastBattleAt);
            foreach (var m in snap.ModelMatchups)
                Url($"/stats/models/{m.ASlug}-vs-{m.BSlug}");
            foreach (var a in snap.Agents)
                Url($"/stats/agents/{a.Slug}", lastmod: a.LastBattleAt);
            foreach (var m in snap.AgentMatchups)
                Url($"/stats/agents/{m.ASlug}-vs-{m.BSlug}");

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
