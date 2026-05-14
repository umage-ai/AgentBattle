using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class StatsCacheTests
{
    [Fact]
    public async Task GetAsync_recomputes_when_battles_directory_mtime_changes()
    {
        var battlesDir = System.IO.Directory.CreateTempSubdirectory().FullName;
        var agentsDir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(battlesDir, "first.jsonl"),
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"first","config_snapshot":"{}","agents":[]}""" + "\n" +
                """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{},"ranking":[]}""");

            var archive = new BattleArchive(battlesDir);
            var registry = new AgentRegistry(agentsDir);
            var cache = new StatsCache(archive, registry, new StatsAggregator());

            var snap1 = await cache.GetAsync();
            var snap2 = await cache.GetAsync();
            snap2.Should().BeSameAs(snap1);

            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(battlesDir, "second.jsonl"),
                """{"t":"battle_started","ts":"2026-05-13T19:00:00Z","battle_id":"second","config_snapshot":"{}","agents":[]}""" + "\n" +
                """{"t":"battle_ended","ts":"2026-05-13T19:42:00Z","final_stacks":{},"ranking":[]}""");

            var snap3 = await cache.GetAsync();
            snap3.Should().NotBeSameAs(snap1);
        }
        finally
        {
            System.IO.Directory.Delete(battlesDir, recursive: true);
            System.IO.Directory.Delete(agentsDir, recursive: true);
        }
    }
}
