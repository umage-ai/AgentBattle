using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Recording;

namespace AgentBattle.Orchestrator.TurnLoop;

public sealed class TurnRunner(IBattleEventSink sink, System.TimeProvider time, OpponentTracker? tracker = null, AgentNotebook? notebook = null)
{
    public async System.Threading.Tasks.Task RunOneTurnAsync(
        int seat,
        AgentSession session,
        System.Func<int, System.Threading.CancellationToken, System.Threading.Tasks.Task<PokerState>> getState,
        System.Func<PokerAction, System.Threading.CancellationToken, System.Threading.Tasks.Task<(bool Ok, string? Error)>> apply,
        System.Threading.CancellationToken ct = default)
    {
        var state = await getState(seat, ct);
        await sink.WriteAsync(new BattleEvent.AgentTurnStarted(time.GetUtcNow(), state.HandNo, seat, state), ct);

        string? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var msg = PromptBuilder.Turn(state, lastError, tracker: tracker, notebook: notebook);
            var reply = await session.SendUserAsync(msg, ct);

            await sink.WriteAsync(new BattleEvent.AgentThoughts(time.GetUtcNow(), state.HandNo, seat, reply.Content ?? "", reply.Tokens, attempt), ct);

            if (reply.ToolCalls.Count == 0)
            {
                lastError = "no_tool_call";
                await sink.WriteAsync(new BattleEvent.AgentActionRejected(time.GetUtcNow(), state.HandNo, seat, "(none)", null, "no_tool_call", attempt), ct);
                continue;
            }

            var (action, parseErr) = ActionParser.Parse(reply.ToolCalls[0], seat);
            if (action == null)
            {
                lastError = parseErr;
                await sink.WriteAsync(new BattleEvent.AgentActionRejected(time.GetUtcNow(), state.HandNo, seat, reply.ToolCalls[0].Name, null, parseErr!, attempt), ct);
                continue;
            }

            var toCallBefore = state.ToCall;
            var (ok, applyErr) = await apply(action, ct);
            if (ok)
            {
                var actionName = ActionName(action);
                var amount = AmountOf(action);
                await sink.WriteAsync(new BattleEvent.AgentAction(time.GetUtcNow(), state.HandNo, seat, actionName, amount, attempt, null), ct);
                tracker?.OnAction(state.HandNo, seat, state.Street, actionName, amount, toCallBefore);
                return;
            }
            lastError = applyErr;
            await sink.WriteAsync(new BattleEvent.AgentActionRejected(time.GetUtcNow(), state.HandNo, seat, ActionName(action), AmountOf(action), applyErr!, attempt), ct);
        }

        // Forced default: check if legal else fold.
        var forced = state.Legal.CanCheck ? (PokerAction)new PokerAction.Check(seat) : new PokerAction.Fold(seat);
        await apply(forced, ct);
        var forcedName = ActionName(forced);
        await sink.WriteAsync(new BattleEvent.AgentAction(time.GetUtcNow(), state.HandNo, seat, forcedName, AmountOf(forced), 4, "retries_exhausted"), ct);
        tracker?.OnAction(state.HandNo, seat, state.Street, forcedName, AmountOf(forced), state.ToCall);
    }

    private static string ActionName(PokerAction a) => a switch
    {
        PokerAction.Fold => "fold",
        PokerAction.Check => "check",
        PokerAction.Call => "call",
        PokerAction.Raise => "raise",
        PokerAction.AllIn => "all_in",
        _ => "unknown"
    };

    private static int? AmountOf(PokerAction a) => a is PokerAction.Raise r ? r.Amount : null;
}
