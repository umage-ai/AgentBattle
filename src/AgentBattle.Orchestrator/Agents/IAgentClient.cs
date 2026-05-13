namespace AgentBattle.Orchestrator.Agents;

public sealed record AgentMessage(string Role, string Content);
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);
public sealed record ToolCall(string Name, string ArgumentsJson);
public sealed record AgentReply(string? Content, IReadOnlyList<ToolCall> ToolCalls, int Tokens);

public interface IAgentClient
{
    System.Threading.Tasks.Task<AgentReply> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        System.Threading.CancellationToken ct = default);
}
