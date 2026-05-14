using AgentBattle.Domain.Battles;
using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class StatsAggregatorTests
{
    private static AgentProfile Profile(string id, string display, string model) =>
        new(id, display, BaseUrl: "", Model: model, ApiKeyEnv: "");

    private static BattleSummary Battle(
        string id,
        System.DateTimeOffset startedAt,
        (int seat, string agentId, string display)[] seats,
        (int seat, int chips, string agentId)[] ranking,
        int startingStack = 1000) =>
        new(
            BattleId: id,
            FilePath: $"/tmp/{id}.jsonl",
            StartedAt: startedAt,
            AgentDisplayNames: seats.Select(s => s.display).ToArray(),
            WinnerAgentId: ranking.OrderByDescending(r => r.chips).First().agentId,
            IsComplete: true,
            StartingStack: startingStack,
            SeatedAgents: seats.Select(s => new SeatedAgent(s.seat, s.agentId, s.display)).ToArray(),
            Ranking: ranking.Select(r => new RankEntry(r.seat, r.chips, r.agentId)).ToArray());

    [Fact]
    public void Single_battle_records_one_win_and_one_loss_per_axis()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
            ["b"] = Profile("b", "Bravo", "claude-haiku")
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo")],
                ranking: [(0, 1200, "a"), (1, 800, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        snap.Models.Should().HaveCount(2);
        snap.Models.Single(m => m.Slug == "gpt-4o-mini").Wins.Should().Be(1.0);
        snap.Models.Single(m => m.Slug == "gpt-4o-mini").Battles.Should().Be(1);
        snap.Models.Single(m => m.Slug == "claude-haiku").Wins.Should().Be(0.0);
        snap.Models.Single(m => m.Slug == "claude-haiku").Battles.Should().Be(1);

        snap.Agents.Should().HaveCount(2);
        snap.Agents.Single(a => a.Slug == "alpha").Wins.Should().Be(1.0);
        snap.Agents.Single(a => a.Slug == "bravo").Wins.Should().Be(0.0);

        snap.ModelMatchups.Should().HaveCount(1);
        var matchup = snap.ModelMatchups[0];
        matchup.ASlug.Should().Be("claude-haiku");
        matchup.BSlug.Should().Be("gpt-4o-mini");
        matchup.AWins.Should().Be(0.0);
        matchup.BWins.Should().Be(1.0);
        matchup.BattleCount.Should().Be(1);
    }

    [Fact]
    public void Tie_at_first_place_produces_half_wins()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
            ["b"] = Profile("b", "Bravo", "claude-haiku")
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo")],
                ranking: [(0, 1000, "a"), (1, 1000, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        snap.Models.Single(m => m.Slug == "gpt-4o-mini").Wins.Should().Be(0.5);
        snap.Models.Single(m => m.Slug == "claude-haiku").Wins.Should().Be(0.5);
        snap.ModelMatchups[0].AWins.Should().Be(0.5);
        snap.ModelMatchups[0].BWins.Should().Be(0.5);
    }

    [Fact]
    public void H2H_uses_relative_chip_rank_not_overall_winner()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
            ["b"] = Profile("b", "Bravo", "claude-haiku"),
            ["c"] = Profile("c", "Charlie", "third-model")
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo"), (2, "c", "Charlie")],
                ranking: [(2, 1500, "c"), (0, 1000, "a"), (1, 500, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        var ab = snap.ModelMatchups.Single(m =>
            (m.ASlug == "claude-haiku" && m.BSlug == "gpt-4o-mini")
            || (m.ASlug == "gpt-4o-mini" && m.BSlug == "claude-haiku"));
        var aIsAlpha = ab.ASlug == "gpt-4o-mini";
        (aIsAlpha ? ab.AWins : ab.BWins).Should().Be(1.0);
        (aIsAlpha ? ab.BWins : ab.AWins).Should().Be(0.0);
    }

    [Fact]
    public void Missing_agent_profile_falls_back_to_unknown_model_bucket()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a"] = Profile("a", "Alpha", "gpt-4o-mini"),
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a", "Alpha"), (1, "b", "Bravo")],
                ranking: [(0, 1200, "a"), (1, 800, "b")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        snap.Models.Should().NotContain(m => m.Slug == "unknown");
        snap.Agents.Should().HaveCount(2);
        snap.Agents.Single(a => a.Slug == "bravo").Wins.Should().Be(0.0);
    }

    [Fact]
    public void Same_model_in_two_seats_counts_both_as_that_model_battles()
    {
        var profiles = new Dictionary<string, AgentProfile>
        {
            ["a1"] = Profile("a1", "Alpha-1", "gpt-4o-mini"),
            ["a2"] = Profile("a2", "Alpha-2", "gpt-4o-mini"),
        };
        var battles = new[]
        {
            Battle("1", System.DateTimeOffset.UtcNow,
                seats: [(0, "a1", "Alpha-1"), (1, "a2", "Alpha-2")],
                ranking: [(0, 1200, "a1"), (1, 800, "a2")])
        };

        var snap = new StatsAggregator().Build(battles, profiles);

        var m = snap.Models.Single(m => m.Slug == "gpt-4o-mini");
        m.Battles.Should().Be(1);
        m.Wins.Should().Be(1.0);
        snap.ModelMatchups.Should().BeEmpty();
        snap.AgentMatchups.Should().HaveCount(1);
    }
}
