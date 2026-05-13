using AgentBattle.Domain.Battles;

namespace AgentBattle.Orchestrator.Recording;

public interface IBattleEventSink : System.IAsyncDisposable
{
    System.Threading.Tasks.Task WriteAsync(BattleEvent e, System.Threading.CancellationToken ct = default);
}
