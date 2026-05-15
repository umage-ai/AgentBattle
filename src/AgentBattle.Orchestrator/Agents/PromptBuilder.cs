using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

public static class PromptBuilder
{
    public const int DefaultActionWindow = 8;

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

        Baseline strategy (your persona may override)
        - Most hands should be folded preflop, especially from early position. Strong starters: high pairs (AA, KK, QQ, JJ, TT) and AK. Decent: medium pairs, AQ/AJ, suited connectors.
        - Position matters: be tighter out of position, looser on the button.
        - Compare pot odds to your equity: if calling 20 to win 100, you only need to be best ~17% of the time to break even.

        Psychological play — this is the point of the experiment
        - You will be shown per-opponent stats accumulated across the match: VPIP% (how often they enter pots), PFR% (preflop raise rate), aggression factor (raises÷calls), fold-to-bet%, and showdown count. Use them to put each active opponent on a range.
        - You will also see the last few showdowns with everyone's revealed cards. These tell you who calls light, who bluffs, who only shows down monsters.
        - Think about your own image: what does your line LOOK like from their side, given how you've been playing? Tight players' bluffs get more respect; loose players' value bets get called down.
        - Bluff with a balanced frequency. Pure value-only is exploitable (opponents fold to all your bets); pure bluff is exploitable (opponents call you down). Mix so neither folding nor calling is automatic.
        - Look for spots to deceive: slow-play a monster against an aggressor, semi-bluff with draws on scary boards, three-bet light against a known nit, check-raise against a habitual c-bettor.

        Between hands
        - After each hand resolves, you will be given a chance to call a `take_note` tool to record a short observation for yourself. Examples of useful notes: "<opponent name> c-bet every flop", "<opponent name> went all-in light", "<opponent name> chased a flush draw and missed". Only YOU will see your own notes; they persist for the rest of the match. Use the actual seat numbers and player names from this match — never hallucinate seats that don't exist. Keep notes terse and exploit-focused.

        How to reply on your action turn — IMPORTANT
        - Your reply MUST contain reasoning prose AND exactly one action tool call. Replies with only a tool call and no prose will be treated as invalid.
        - Structure your prose (3–5 sentences):
          1. Your hand and board read.
          2. The range you put each *active* opponent on, citing their stats and this-hand actions.
          3. Your own image — what your line looks like from their seat right now.
          4. The action you're choosing and why (value, bluff, pot-control, fold equity).
        - Even when the decision is obvious ("fold junk preflop"), write at least one sentence each for opponent read and image. Reasoning is the experiment — silence loses information.

        {profile.PersonaPrompt}
        """;

    public static string Turn(
        PokerState state,
        string? retryError,
        int actionWindow = DefaultActionWindow,
        OpponentTracker? tracker = null,
        AgentNotebook? notebook = null)
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

        if (tracker != null)
        {
            sb.AppendLine("Opponent profiles (across match so far):");
            var opponents = state.Seats.Where(s => s.Seat != state.Seat && !s.IsInactive).ToArray();
            if (opponents.Length == 0)
            {
                sb.AppendLine("  (no opponents)");
            }
            else
            {
                foreach (var op in opponents)
                {
                    var st = tracker.Get(op.Seat);
                    var vpip = st.VpipPct.HasValue ? $"{st.VpipPct}%" : "—";
                    var pfr  = st.PfrPct.HasValue  ? $"{st.PfrPct}%"  : "—";
                    var af   = st.AggressionFactor.HasValue ? st.AggressionFactor.Value.ToString("0.0") : "—";
                    var fb   = st.FoldToBetPct.HasValue ? $"{st.FoldToBetPct}%" : "—";
                    var sd   = $"{st.ShowdownsWon}/{st.Showdowns}";
                    var status = op.HasFolded ? " [folded this hand]" : (op.IsAllIn ? " [all-in]" : "");
                    sb.AppendLine($"  seat {op.Seat} ({op.AgentDisplayName}){status}: VPIP {vpip}, PFR {pfr}, AF {af}, fold-to-bet {fb}, SD {sd}");
                }
            }
            sb.AppendLine();

            if (tracker.RecentShowdowns.Count > 0)
            {
                sb.AppendLine("Recent showdowns (revealed cards):");
                foreach (var sd in tracker.RecentShowdowns.TakeLast(4))
                {
                    var board = sd.Community.Count == 0 ? "(no board)" : string.Join(" ", sd.Community);
                    sb.AppendLine($"  hand {sd.HandNo}, board {board}");
                    foreach (var (seat, hole) in sd.Reveals)
                    {
                        var who = state.Seats.FirstOrDefault(s => s.Seat == seat)?.AgentDisplayName ?? $"seat {seat}";
                        var win = sd.Winners.Contains(seat) ? " (won)" : "";
                        sb.AppendLine($"    seat {seat} ({who}): {string.Join(" ", hole)}{win}");
                    }
                }
                sb.AppendLine();
            }
        }

        if (notebook != null)
        {
            var myNotes = notebook.For(state.Seat);
            if (myNotes.Count > 0)
            {
                sb.AppendLine("Your private notes from earlier hands:");
                foreach (var n in myNotes)
                    sb.AppendLine($"  [hand {n.HandNo}] {n.Text}");
                sb.AppendLine();
            }
        }

        var legal = new List<string>();
        if (state.Legal.CanCheck) legal.Add("check");
        if (state.Legal.CanCall) legal.Add($"call ({state.Legal.CallAmount})");
        if (state.Legal.CanRaise) legal.Add($"raise (total {state.Legal.MinRaiseTotal}–{state.Legal.MaxRaiseTotal})");
        if (state.Legal.CanFold) legal.Add("fold");
        sb.AppendLine($"Legal: {string.Join(", ", legal)}.");
        sb.AppendLine();
        sb.AppendLine("Reply with your reasoning (opponent ranges + your image + your plan), then call exactly one action tool.");
        return sb.ToString();
    }

    /// <summary>
    /// Prompt sent between hands inviting the agent to record a short private note.
    /// Calling take_note is optional — the agent can also reply with no tool call to skip.
    /// </summary>
    public static string NoteTurn(
        int handNo,
        IReadOnlyList<SeatSummary> seats,
        IReadOnlyDictionary<int, IReadOnlyList<AgentBattle.Domain.Cards.Card>>? reveals,
        IReadOnlyList<int> winners,
        int mySeat,
        OpponentTracker tracker,
        AgentNotebook notebook)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Hand {handNo} is over.");
        if (reveals != null && reveals.Count > 0)
        {
            sb.AppendLine("Showdown reveals:");
            foreach (var (seat, hole) in reveals)
            {
                var who = seats.FirstOrDefault(s => s.Seat == seat)?.AgentDisplayName ?? $"seat {seat}";
                var win = winners.Contains(seat) ? " (won)" : "";
                sb.AppendLine($"  seat {seat} ({who}): {string.Join(" ", hole)}{win}");
            }
        }
        else if (winners.Count > 0)
        {
            sb.AppendLine($"Winner(s) without showdown: {string.Join(", ", winners.Select(w => $"seat {w}"))}.");
        }
        sb.AppendLine();
        sb.AppendLine("Current stacks: " + string.Join(" | ", seats.Select(s =>
            $"seat {s.Seat} ({s.AgentDisplayName}) {s.Stack}")));
        sb.AppendLine();

        var myNotes = notebook.For(mySeat);
        if (myNotes.Count > 0)
        {
            sb.AppendLine("Your existing notes:");
            foreach (var n in myNotes)
                sb.AppendLine($"  [hand {n.HandNo}] {n.Text}");
            sb.AppendLine();
        }

        sb.AppendLine($"You are seat {mySeat}. If you want to add a private note for yourself (something you've learned about an opponent that you can exploit later), call the `take_note` tool with a short `text` argument (one sentence max). If you don't have anything to add, reply with no tool call.");
        return sb.ToString();
    }
}
