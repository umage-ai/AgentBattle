namespace AgentBattle.Domain.Poker;

public abstract record PokerAction(int Seat)
{
    public sealed record Fold(int Seat) : PokerAction(Seat);
    public sealed record Check(int Seat) : PokerAction(Seat);
    public sealed record Call(int Seat) : PokerAction(Seat);
    public sealed record Raise(int Seat, int Amount) : PokerAction(Seat);
    public sealed record AllIn(int Seat) : PokerAction(Seat);
}
