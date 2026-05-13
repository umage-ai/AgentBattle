using System.Text.Json;
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Json;

namespace AgentBattle.Orchestrator.Recording;

public sealed class JsonlEventSink : IBattleEventSink
{
    private readonly System.IO.StreamWriter _writer;

    public JsonlEventSink(string path)
    {
        var stream = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
        _writer = new System.IO.StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
    }

    public async System.Threading.Tasks.Task WriteAsync(BattleEvent e, System.Threading.CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize<BattleEvent>(e, BattleEventJsonOptions.Default);
        await _writer.WriteLineAsync(json.AsMemory(), ct);
    }

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }
}
