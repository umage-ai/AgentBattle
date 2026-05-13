using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

public static class PromptBuilder
{
    public const int DefaultActionWindow = 6;

    public static string SystemPrompt(AgentProfile profile, int seat) => $"""
        You are {profile.DisplayName}, playing 6-max No-Limit Texas Hold'em. You are seat {seat}.

        Rules summary
        - Hand rankings, low to high: high card, pair, two pair, three of a kind, straight, flush, full house, four of a kind, straight flush.
        - On your turn you must call exactly one action tool: fold, check, call, raise, or all_in.
        - check is only legal when there is no outstanding bet for you to match.
        - call always matches the current bet to call (no amount needed).
        - raise's `amount` argument is the new TOTAL bet level for the street — not the increment. It must fall between the min_raise_total and max_raise_total reported in your state.
        - all_in pushes your entire stack into the pot.
        - Each new street resets the betting; only chips already in the pot from earlier streets stay there.
        - You only ever see your own hole cards. Other seats' hole cards are hidden until showdown.

        Match setup
        - 6-max table, starting stacks 1000, blinds 10/20, no escalation, fixed number of hands.

        Basic strategy (baseline — your persona may override)
        - Most hands should be folded preflop, especially from early position. Strong starters: high pairs (AA, KK, QQ, JJ, TT) and AK. Decent: medium pairs, AQ/AJ, suited connectors. Weak/junk: low offsuit cards.
        - Position matters: acting later in a betting round gives you information. Be tighter out of position, looser on the button.
        - Compare pot odds to your equity: if calling 20 to win 100, you only need to be best ~17% of the time to break even.
        - Don't put chips in unless you can justify it with hand strength, fold equity, or position. When unsure and free, check; when unsure and facing a bet, leaning fold is usually right.
        - Vary your play. Predictable agents get exploited.

        How to reply
        - First, briefly state your reasoning in plain prose: hand strength, position, pot odds, read on opponents.
        - Then call exactly one action tool to commit your decision.

        {profile.PersonaPrompt}
        """;

    public static string Turn(PokerState state, string? retryError, int actionWindow = DefaultActionWindow)
    {
        var sb = new System.Text.StringBuilder();
        if (retryError != null)
            sb.AppendLine($"Your previous action was rejected: {retryError}. Try again.").AppendLine();

        var seatName = state.Seats.FirstOrDefault(s => s.Seat == state.Seat)?.AgentDisplayName ?? $"seat {state.Seat}";
        sb.AppendLine($"Hand {state.HandNo}, {state.Street.ToString().ToLowerInvariant()}. Your turn (seat {state.Seat} — {seatName}).");
        sb.AppendLine();
        sb.AppendLine($"Your hole cards: {string.Join(" ", state.HoleCards)}");
        sb.AppendLine($"Community: {(state.Community.Count == 0 ? "(none yet)" : string.Join(" ", state.Community))}");
        sb.AppendLine($"Your stack: {state.MyStack}. Pot: {state.Pot}. To call: {state.ToCall}.");
        if (state.Legal.CanRaise) sb.AppendLine($"Min raise to: {state.Legal.MinRaiseTotal}. Max (all-in): {state.Legal.MaxRaiseTotal}.");
        sb.AppendLine();

        sb.AppendLine($"Recent actions this hand (most recent last):");
        if (state.ActionLog.Count == 0)
        {
            sb.AppendLine("  (no actions yet)");
        }
        else
        {
            var window = state.ActionLog.Skip(System.Math.Max(0, state.ActionLog.Count - actionWindow));
            foreach (var entry in window)
            {
                var entrySeatName = state.Seats.FirstOrDefault(s => s.Seat == entry.Seat)?.AgentDisplayName ?? $"seat {entry.Seat}";
                var amount = entry.Amount != null ? " " + entry.Amount : "";
                sb.AppendLine($"  {entry.Street.ToString().ToLowerInvariant(),-8} seat {entry.Seat} ({entrySeatName}) {entry.Action}{amount}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("Stacks: " + string.Join(" | ", state.Seats.Select(s =>
            $"seat {s.Seat} ({s.AgentDisplayName}) {s.Stack}{(s.HasFolded ? " folded" : "")}{(s.IsAllIn ? " all-in" : "")}")));
        sb.AppendLine();

        var legal = new List<string>();
        if (state.Legal.CanCheck) legal.Add("check");
        if (state.Legal.CanCall) legal.Add($"call ({state.Legal.CallAmount})");
        if (state.Legal.CanRaise) legal.Add($"raise (total {state.Legal.MinRaiseTotal}–{state.Legal.MaxRaiseTotal})");
        if (state.Legal.CanFold) legal.Add("fold");
        sb.AppendLine($"Legal: {string.Join(", ", legal)}.");
        sb.AppendLine();
        sb.AppendLine("Reply with your reasoning, then call exactly one action tool.");
        return sb.ToString();
    }
}
