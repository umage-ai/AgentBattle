using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Agents;

public class ActionParserTests
{
    [Theory]
    [InlineData("fold")]
    [InlineData("check")]
    [InlineData("call")]
    [InlineData("all_in")]
    public void Parses_action_for_seat(string name)
    {
        var (action, error) = ActionParser.Parse(new ToolCall(name, "{}"), seat: 4);
        action.Should().NotBeNull();
        error.Should().BeNull();
        action!.Seat.Should().Be(4);
    }

    [Fact]
    public void Parses_raise_with_amount()
    {
        var (action, error) = ActionParser.Parse(new ToolCall("raise", "{\"amount\":60}"), seat: 4);
        error.Should().BeNull();
        action.Should().BeOfType<PokerAction.Raise>().Which.Amount.Should().Be(60);
    }

    [Fact]
    public void Returns_error_for_unknown_tool()
    {
        var (action, error) = ActionParser.Parse(new ToolCall("nuke", "{}"), seat: 4);
        action.Should().BeNull();
        error.Should().Contain("unknown_tool");
    }

    [Fact]
    public void Returns_error_for_raise_without_amount()
    {
        var (action, error) = ActionParser.Parse(new ToolCall("raise", "{}"), seat: 4);
        action.Should().BeNull();
        error.Should().Contain("missing_amount");
    }

    [Fact]
    public void Parses_raise_with_string_amount()
    {
        // Some local LLMs emit numbers as strings — accept them gracefully
        // rather than throwing out of the retry loop.
        var (action, error) = ActionParser.Parse(new ToolCall("raise", "{\"amount\":\"75\"}"), seat: 4);
        error.Should().BeNull();
        action.Should().BeOfType<PokerAction.Raise>().Which.Amount.Should().Be(75);
    }

    [Fact]
    public void Returns_error_for_non_numeric_string_amount()
    {
        var (action, error) = ActionParser.Parse(new ToolCall("raise", "{\"amount\":\"big\"}"), seat: 4);
        action.Should().BeNull();
        error.Should().Contain("amount_not_numeric");
    }

    [Fact]
    public void Returns_error_for_amount_of_wrong_type()
    {
        var (action, error) = ActionParser.Parse(new ToolCall("raise", "{\"amount\":true}"), seat: 4);
        action.Should().BeNull();
        error.Should().Contain("amount_wrong_type");
    }
}
