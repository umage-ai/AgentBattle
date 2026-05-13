namespace AgentBattle.Domain.Poker;

public sealed record LegalActions(
    bool CanCheck,
    bool CanCall, int CallAmount,
    bool CanRaise, int MinRaiseTotal, int MaxRaiseTotal,
    bool CanFold);
