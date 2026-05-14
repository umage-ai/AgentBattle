using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentBattle.Web.Tests.Pages;

public class SitemapTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SitemapTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Sitemap_returns_xml_with_core_routes()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = System.IO.Path.Combine(dir, "battle.jsonl");
            await System.IO.File.WriteAllLinesAsync(path, new[]
            {
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"x","config_snapshot":"{\"game\":\"poker-6max\",\"hands\":3,\"starting_stack\":1000,\"blinds\":{\"small\":10,\"big\":20},\"seats\":[]}","agents":[{"seat":0,"id":"a","display_name":"Anna"},{"seat":1,"id":"b","display_name":"Bob"}]}""",
                """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
            });

            using var client = _factory.WithWebHostBuilder(b => b.UseSetting("Paths:BattlesDirectory", dir)).CreateClient();
            var resp = await client.GetAsync("/sitemap.xml");

            resp.IsSuccessStatusCode.Should().BeTrue();
            resp.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
            var body = await resp.Content.ReadAsStringAsync();
            body.Should().Contain("<urlset");
            body.Should().Contain("/stats");
            body.Should().Contain("/stats/agents/anna");
            body.Should().Contain("/stats/agents/bob");
            body.Should().Contain("/stats/agents/anna-vs-bob");
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
