using System.Text.Json;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

public static class ActionParser
{
    public static (PokerAction? Action, string? Error) Parse(ToolCall call, int seat)
    {
        return call.Name switch
        {
            "fold"   => (new PokerAction.Fold(seat), null),
            "check"  => (new PokerAction.Check(seat), null),
            "call"   => (new PokerAction.Call(seat), null),
            "all_in" => (new PokerAction.AllIn(seat), null),
            "raise"  => ParseRaise(seat, call.ArgumentsJson),
            _ => ((PokerAction?)null, $"unknown_tool: {call.Name}")
        };
    }

    private static (PokerAction?, string?) ParseRaise(int seat, string args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (!doc.RootElement.TryGetProperty("amount", out var amt))
                return (null, "missing_amount");
            return (new PokerAction.Raise(seat, amt.GetInt32()), null);
        }
        catch (JsonException ex)
        {
            return (null, $"invalid_arguments_json: {ex.Message}");
        }
    }
}
