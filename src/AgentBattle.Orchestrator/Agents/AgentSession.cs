namespace AgentBattle.Orchestrator.Agents;

/// <summary>
/// Per-agent identity carrier. Holds the system prompt + tool definitions but does NOT
/// accumulate chat history across turns. Each call to <see cref="SendUserAsync"/> sends
/// exactly [system, user] to the underlying client. The per-turn user message must be
/// self-contained — see <see cref="PromptBuilder"/> for the current state + recent-actions
/// window we include.
/// </summary>
/// <remarks>
/// Stateless turn prompts keep token usage bounded. Earlier versions accumulated full
/// chat history, which grew unboundedly across hands and pushed long matches out of the
/// context window. Cross-hand memory (if needed) should be passed through the prompt
/// as a structured summary rather than raw conversation accumulation.
/// </remarks>
public sealed class AgentSession(string agentId, string displayName, IAgentClient client, IReadOnlyList<ToolDefinition> tools, string systemPrompt)
{
    public string AgentId { get; } = agentId;
    public string DisplayName { get; } = displayName;

    private readonly AgentMessage _system = new("system", systemPrompt);

    public System.Threading.Tasks.Task<AgentReply> SendUserAsync(string content, System.Threading.CancellationToken ct = default)
        => client.ChatAsync([_system, new AgentMessage("user", content)], tools, ct);
}
