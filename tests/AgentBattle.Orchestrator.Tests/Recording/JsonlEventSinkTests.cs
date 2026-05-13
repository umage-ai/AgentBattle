using AgentBattle.Domain.Battles;
using AgentBattle.Orchestrator.Recording;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Recording;

public class JsonlEventSinkTests
{
    [Fact]
    public async Task Writes_one_line_per_event_with_type_discriminator()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            await using (var sink = new JsonlEventSink(path))
            {
                await sink.WriteAsync(new BattleEvent.HandStarted(System.DateTimeOffset.UtcNow, 1, 0, 1, 2, []));
                await sink.WriteAsync(new BattleEvent.HandEnded(System.DateTimeOffset.UtcNow, 1, new Dictionary<int, int> { [0] = 1000, [1] = 1000 }));
            }
            var lines = await System.IO.File.ReadAllLinesAsync(path);
            lines.Should().HaveCount(2);
            lines[0].Should().Contain("\"t\":\"hand_started\"");
            lines[1].Should().Contain("\"t\":\"hand_ended\"");
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }
}
