using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Mcp;

/// <summary>
/// Abstraction over a poker backend used by <see cref="BattleOrchestrator"/>.
/// Production uses <see cref="McpGameClient"/> (stdio MCP child process); tests can plug an
/// in-process implementation wrapping a <c>PokerGame</c> directly to avoid spawning a process.
/// </summary>
public interface IGameBackend
{
    System.Threading.Tasks.Task ConfigureGameAsync(int[] seats, int[] startingStacks, string[] agentNames, int sb, int bb, int btn, int seed, System.Threading.CancellationToken ct);
    System.Threading.Tasks.Task<int> StartHandAsync(System.Threading.CancellationToken ct);
    System.Threading.Tasks.Task<PokerState> GetMyStateAsync(int seat, System.Threading.CancellationToken ct);
    System.Threading.Tasks.Task<(bool Ok, string? Error)> ApplyAsync(PokerAction action, System.Threading.CancellationToken ct);
    System.Threading.Tasks.Task<McpShowdownResult> ResolveShowdownAsync(System.Threading.CancellationToken ct);
    System.Threading.Tasks.Task<IReadOnlyDictionary<int, IReadOnlyList<Card>>> GodViewRevealAsync(System.Threading.CancellationToken ct);

    /// <summary>
    /// Last-known acting seat. Updated after every state-fetch and successful <see cref="ApplyAsync"/>.
    /// </summary>
    int CurrentSeat { get; }

    /// <summary>
    /// Last-known street. Updated after every state-fetch and successful <see cref="ApplyAsync"/>.
    /// </summary>
    Street CurrentStreet { get; }
}
