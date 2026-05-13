using AgentBattle.Domain.Cards;

namespace AgentBattle.Domain.Poker;

public sealed record PokerState(
    int HandNo,
    Street Street,
    int Seat,
    IReadOnlyList<Card> HoleCards,
    IReadOnlyList<Card> Community,
    int MyStack,
    int MyCurrentBet,
    int Pot,
    int ToCall,
    IReadOnlyList<SeatSummary> Seats,
    IReadOnlyList<ActionLogEntry> ActionLog,
    int CurrentSeat,
    LegalActions Legal);

public sealed record SeatSummary(int Seat, string AgentDisplayName, int Stack, int CurrentBet, bool HasFolded, bool IsAllIn, bool IsInactive);

public sealed record ActionLogEntry(int Seat, string Action, int? Amount, Street Street);
