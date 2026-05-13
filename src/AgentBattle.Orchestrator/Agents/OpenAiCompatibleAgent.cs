using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AgentBattle.Orchestrator.Agents;

public sealed class OpenAiCompatibleAgent(HttpClient http, string baseUrl, string model, string apiKey, double temperature, int maxTokens) : IAgentClient
{
    public async System.Threading.Tasks.Task<AgentReply> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        System.Threading.CancellationToken ct = default)
    {
        var req = new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            tools = tools.Count == 0 ? null : tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonNode.Parse(t.ParametersJsonSchema)
                }
            }).ToArray<object>(),
            temperature,
            max_tokens = maxTokens
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = JsonContent.Create(req)
        };
        if (!string.IsNullOrEmpty(apiKey))
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await http.SendAsync(message, ct);
        resp.EnsureSuccessStatusCode();
        var bodyText = await resp.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(bodyText)!;

        var msg = node["choices"]![0]!["message"]!;
        var content = msg["content"]?.GetValue<string>();
        var toolCallsNode = msg["tool_calls"]?.AsArray();
        var toolCalls = toolCallsNode == null
            ? (IReadOnlyList<ToolCall>)System.Array.Empty<ToolCall>()
            : toolCallsNode.Select(tc => new ToolCall(
                tc!["function"]!["name"]!.GetValue<string>(),
                tc["function"]!["arguments"]!.GetValue<string>())).ToArray();
        var tokens = node["usage"]?["total_tokens"]?.GetValue<int>() ?? 0;
        return new AgentReply(content, toolCalls, tokens);
    }
}
