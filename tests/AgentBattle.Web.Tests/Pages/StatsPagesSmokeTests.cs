using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentBattle.Web.Tests.Pages;

public class StatsPagesSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public StatsPagesSmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Stats_index_returns_200()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Stats");
    }

    [Fact]
    public async Task Stats_models_index_returns_200()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats/models");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("model leaderboard");
    }

    [Fact]
    public async Task Stats_models_detail_404_for_unknown_slug()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats/models/this-model-does-not-exist-anywhere-12345");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stats_models_versus_unknown_halves_404s()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats/models/zzz-vs-aaa");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stats_agents_index_returns_200()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats/agents");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Agent leaderboard");
    }

    private WebApplicationFactory<Program> WithTempBattles(string battlesDir) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Paths:BattlesDirectory", battlesDir);
        });

    [Fact]
    public async Task Stats_agents_versus_canonicalises_via_301()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = System.IO.Path.Combine(dir, "battle.jsonl");
            await System.IO.File.WriteAllLinesAsync(path, new[]
            {
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"x","config_snapshot":"{\"game\":\"poker-6max\",\"hands\":3,\"starting_stack\":1000,\"blinds\":{\"small\":10,\"big\":20},\"seats\":[]}","agents":[{"seat":0,"id":"a","display_name":"Zelda"},{"seat":1,"id":"b","display_name":"Anna"}]}""",
                """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
            });

            using var client = WithTempBattles(dir).CreateClient(new() { AllowAutoRedirect = false });
            var resp = await client.GetAsync("/stats/agents/zelda-vs-anna");
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.MovedPermanently);
            resp.Headers.Location!.ToString().Should().Be("/stats/agents/anna-vs-zelda");

            var canonical = await client.GetAsync("/stats/agents/anna-vs-zelda");
            canonical.IsSuccessStatusCode.Should().BeTrue();
            var body = await canonical.Content.ReadAsStringAsync();
            body.Should().Contain("Anna").And.Contain("Zelda");
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Stats_agents_detail_404_for_unknown_slug()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats/agents/this-agent-does-not-exist-anywhere-12345");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stats_agents_unknown_matchup_halves_404s()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/stats/agents/zzz-vs-aaa");
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
