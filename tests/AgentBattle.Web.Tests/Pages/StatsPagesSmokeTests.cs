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
}
