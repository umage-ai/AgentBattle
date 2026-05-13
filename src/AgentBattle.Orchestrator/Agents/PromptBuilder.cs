using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

public static class PromptBuilder
{
    public static string System(AgentProfile profile, int seat) => $"""
        You are {profile.DisplayName}, playing 6-max No-Limit Texas Hold'em.
        Starting stacks: 1000. Blinds: 10/20. 50 hands total. You are seat {seat}.
        On each turn you will receive your scoped game state. Respond with your
        reasoning in natural prose, then call exactly one action tool: fold, check,
        call, raise, or all_in.

        {profile.PersonaPrompt}
        """;

    public static string Turn(PokerState state, string? retryError)
    {
        var sb = new System.Text.StringBuilder();
        if (retryError != null)
            sb.AppendLine($"Your previous action was rejected: {retryError}. Try again.").AppendLine();
        sb.AppendLine($"Hand {state.HandNo}, {state.Street.ToString().ToLowerInvariant()}. Your turn.");
        sb.AppendLine();
        sb.AppendLine($"Your hole cards: {string.Join(" ", state.HoleCards)}");
        sb.AppendLine($"Community: {(state.Community.Count == 0 ? "(none yet)" : string.Join(" ", state.Community))}");
        sb.AppendLine($"Your stack: {state.MyStack}");
        sb.AppendLine($"Pot: {state.Pot}");
        sb.AppendLine($"To call: {state.ToCall}");
        if (state.Legal.CanRaise) sb.AppendLine($"Min raise to: {state.Legal.MinRaiseTotal}. Max (all-in): {state.Legal.MaxRaiseTotal}.");
        sb.AppendLine($"Action so far: {(state.ActionLog.Count == 0 ? "(no actions yet)" : string.Join("; ", state.ActionLog.Select(e => $"seat {e.Seat} {e.Action}{(e.Amount != null ? " " + e.Amount : "")}")))}");
        sb.AppendLine();
        var legal = new List<string>();
        if (state.Legal.CanCheck) legal.Add("check");
        if (state.Legal.CanCall) legal.Add($"call ({state.Legal.CallAmount})");
        if (state.Legal.CanRaise) legal.Add($"raise (to a total between {state.Legal.MinRaiseTotal} and {state.Legal.MaxRaiseTotal})");
        if (state.Legal.CanFold) legal.Add("fold");
        sb.AppendLine($"Legal actions: {string.Join(", ", legal)}.");
        sb.AppendLine();
        sb.AppendLine("Reply with your reasoning then call exactly one action tool.");
        return sb.ToString();
    }
}
