using AgentBattle.Domain.Poker;
using AgentBattle.Poker.Mcp.Engine;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AgentBattle.Poker.Mcp.Mcp;

[McpServerToolType]
public sealed class PokerTools
{
    private static PokerGame? _game;

    [McpServerTool, Description("Configure a new poker game for this server instance. Must be called once before any other tool.")]
    public static string ConfigureGame(int[] seats, int[] startingStacks, string[] agentNames, int smallBlind, int bigBlind, int buttonSeat, int deckSeed)
    {
        var stacks = seats.Zip(startingStacks).ToDictionary(p => p.First, p => p.Second);
        var names = seats.Zip(agentNames).ToDictionary(p => p.First, p => p.Second);
        _game = new PokerGame(seats, stacks, names, smallBlind, bigBlind, buttonSeat, deckSeed);
        return Serialize(new { ok = true });
    }

    [McpServerTool, Description("Start the next hand. Posts blinds and deals hole cards. Returns the new hand number.")]
    public static string StartHand()
    {
        Require().StartHand();
        return Serialize(new { ok = true, hand_no = Require().HandNo });
    }

    [McpServerTool, Description("Get game state visible to a specific seat. Hole cards are only returned for the requesting seat.")]
    public static string GetMyState(int seat) => Serialize(Require().GetMyState(seat));

    [McpServerTool, Description("Fold for the given seat. Must be the current seat to act.")]
    public static string Fold(int seat) => ApplyAndSerialize(new PokerAction.Fold(seat));

    [McpServerTool, Description("Check for the given seat. Only legal when no bet is outstanding for this seat.")]
    public static string Check(int seat) => ApplyAndSerialize(new PokerAction.Check(seat));

    [McpServerTool, Description("Call the current bet for the given seat.")]
    public static string Call(int seat) => ApplyAndSerialize(new PokerAction.Call(seat));

    [McpServerTool, Description("Raise to the given total bet level (not increment). Must be at least min_raise_total and at most max_raise_total from get_my_state.")]
    public static string Raise(int seat, int amount) => ApplyAndSerialize(new PokerAction.Raise(seat, amount));

    [McpServerTool, Description("Push all remaining chips into the pot for the given seat.")]
    public static string AllIn(int seat) => ApplyAndSerialize(new PokerAction.AllIn(seat));

    [McpServerTool, Description("Resolve showdown when the current street is Showdown. Returns winners, updated stacks, and revealed hole cards.")]
    public static string ResolveShowdown() => Serialize(Require().ResolveShowdown());

    [McpServerTool, Description("Orchestrator-only: returns every seat's hole cards for logging. Must never be exposed to agents.")]
    public static string GodViewReveal() => Serialize(Require().CurrentHoleCards());

    private static PokerGame Require() => _game ?? throw new System.InvalidOperationException("Call configure_game first");

    private static string ApplyAndSerialize(PokerAction a)
    {
        var r = Require().Apply(a);
        return r.Ok
            ? Serialize(new { ok = true, applied = r.Applied, current_seat = Require().CurrentSeat, street = Require().Street.ToString().ToLowerInvariant() })
            : Serialize(new { ok = false, error = r.Reason, legal = Require().GetMyState(a.Seat).Legal });
    }

    private static string Serialize(object obj)
        => System.Text.Json.JsonSerializer.Serialize(obj, AgentBattle.Domain.Json.BattleEventJsonOptions.Default);
}
