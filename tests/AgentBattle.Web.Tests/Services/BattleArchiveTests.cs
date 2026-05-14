using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class BattleArchiveTests
{
    [Fact]
    public async Task ListBattles_reads_summary_from_first_and_last_events()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = System.IO.Path.Combine(dir, "2026-05-13T1800-abc12345.jsonl");
            await System.IO.File.WriteAllLinesAsync(path, new[]
            {
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"abc12345","config_snapshot":"{\"game\":\"poker-6max\",\"hands\":3,\"starting_stack\":1000,\"blinds\":{\"small\":10,\"big\":20},\"seats\":[]}","agents":[{"seat":0,"id":"a","display_name":"A"},{"seat":1,"id":"b","display_name":"B"}]}""",
                """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
            });

            var archive = new BattleArchive(dir);
            var summaries = await archive.ListBattlesAsync();

            summaries.Should().HaveCount(1);
            var s = summaries[0];
            s.BattleId.Should().Be("abc12345");
            s.AgentDisplayNames.Should().BeEquivalentTo(new[] { "A", "B" });
            s.WinnerAgentId.Should().Be("a");
            s.IsComplete.Should().BeTrue();
            s.StartingStack.Should().Be(1000);
            s.SeatedAgents.Should().HaveCount(2);
            s.SeatedAgents[0].Id.Should().Be("a");
            s.Ranking.Should().HaveCount(2);
            s.Ranking[0].AgentId.Should().Be("a");
            s.Ranking[0].Chips.Should().Be(1200);
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ListBattles_marks_incomplete_when_no_battle_ended()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = System.IO.Path.Combine(dir, "incomplete.jsonl");
            await System.IO.File.WriteAllLinesAsync(path, new[]
            {
                """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"xyz","config_snapshot":"{}","agents":[{"seat":0,"id":"a","display_name":"A"}]}"""
            });

            var archive = new BattleArchive(dir);
            var summaries = await archive.ListBattlesAsync();

            summaries.Should().HaveCount(1);
            summaries[0].IsComplete.Should().BeFalse();
            summaries[0].WinnerAgentId.Should().BeNull();
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ListBattles_returns_empty_when_directory_missing()
    {
        var archive = new BattleArchive(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "does-not-exist-" + System.Guid.NewGuid()));
        var summaries = await archive.ListBattlesAsync();
        summaries.Should().BeEmpty();
    }
}
