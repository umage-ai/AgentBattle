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
}
