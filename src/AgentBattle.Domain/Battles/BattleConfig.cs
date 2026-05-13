namespace AgentBattle.Domain.Battles;

public sealed record BattleConfig(
    string Game,
    int Hands,
    int StartingStack,
    BlindsConfig Blinds,
    IReadOnlyList<SeatAssignment> Seats);

public sealed record BlindsConfig(int Small, int Big);

public sealed record SeatAssignment(int Seat, string Agent);
