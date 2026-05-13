using System.Net;
using System.Net.Http;
using AgentBattle.Orchestrator.Agents;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Agents;

public class OpenAiCompatibleAgentTests
{
    [Fact]
    public async Task Parses_response_with_content_and_tool_call()
    {
        var responseJson = """
        {
          "choices": [{
            "message": {
              "role": "assistant",
              "content": "I think I should raise here.",
              "tool_calls": [{
                "id": "call_1",
                "type": "function",
                "function": { "name": "raise", "arguments": "{\"amount\":60}" }
              }]
            }
          }],
          "usage": { "total_tokens": 142 }
        }
        """;
        var handler = new StubHandler(responseJson);
        var client = new OpenAiCompatibleAgent(new HttpClient(handler), baseUrl: "http://stub/v1", model: "m", apiKey: "k", temperature: 0.7, maxTokens: 1500);

        var reply = await client.ChatAsync(
            [new AgentMessage("system", "you are a player"), new AgentMessage("user", "your turn")],
            [new ToolDefinition("raise", "raise to amount", "{}")]);

        reply.Content.Should().Contain("raise here");
        reply.ToolCalls.Should().HaveCount(1);
        reply.ToolCalls[0].Name.Should().Be("raise");
        reply.ToolCalls[0].ArgumentsJson.Should().Be("{\"amount\":60}");
        reply.Tokens.Should().Be(142);
    }

    [Fact]
    public async Task Returns_empty_tool_calls_when_response_has_only_content()
    {
        var responseJson = """
        {
          "choices": [{
            "message": { "role": "assistant", "content": "I'm thinking..." }
          }],
          "usage": { "total_tokens": 50 }
        }
        """;
        var client = new OpenAiCompatibleAgent(new HttpClient(new StubHandler(responseJson)), "http://stub/v1", "m", "k", 0.7, 1500);
        var reply = await client.ChatAsync([new AgentMessage("user", "hi")], []);
        reply.Content.Should().Contain("thinking");
        reply.ToolCalls.Should().BeEmpty();
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
