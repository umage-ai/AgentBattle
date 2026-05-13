namespace AgentBattle.Orchestrator.Agents;

public sealed class AgentSession(string agentId, string displayName, IAgentClient client, IReadOnlyList<ToolDefinition> tools, string systemPrompt)
{
    public string AgentId { get; } = agentId;
    public string DisplayName { get; } = displayName;
    private readonly List<AgentMessage> _history = [new AgentMessage("system", systemPrompt)];

    public System.Threading.Tasks.Task<AgentReply> SendUserAsync(string content, System.Threading.CancellationToken ct = default)
    {
        _history.Add(new AgentMessage("user", content));
        return client.ChatAsync(_history, tools, ct);
    }

    public void RecordAssistantReply(AgentReply reply)
    {
        var content = reply.Content ?? "";
        if (reply.ToolCalls.Count > 0)
            content += " [called " + string.Join(", ", reply.ToolCalls.Select(t => $"{t.Name}({t.ArgumentsJson})")) + "]";
        _history.Add(new AgentMessage("assistant", content));
    }
}
