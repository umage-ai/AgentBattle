namespace AgentBattle.Poker.Mcp.Engine;

public sealed class BettingRound
{
    private readonly List<int> _active;
    private readonly HashSet<int> _hasActedSinceLastRaise = [];
    public int CurrentBet { get; private set; }
    public int MinRaise { get; private set; }
    public int CurrentSeat { get; private set; }

    public IReadOnlyList<int> ActiveSeats => _active;

    public BettingRound(IReadOnlyList<int> activeSeats, int startSeat, int currentBet, int minRaise)
    {
        _active = new List<int>(activeSeats);
        CurrentBet = currentBet;
        MinRaise = minRaise;
        CurrentSeat = startSeat;
    }

    public bool IsClosed => _active.Count <= 1 || _active.All(s => _hasActedSinceLastRaise.Contains(s));

    public void RecordCheck(int seat) { Acted(seat); Advance(); }
    public void RecordCall(int seat, int amount) { Acted(seat); Advance(); }
    public void RecordFold(int seat) { _active.Remove(seat); Advance(); }

    public void RecordRaiseTo(int seat, int totalBet)
    {
        MinRaise = totalBet - CurrentBet;
        CurrentBet = totalBet;
        _hasActedSinceLastRaise.Clear();
        _hasActedSinceLastRaise.Add(seat);
        Advance();
    }

    private void Acted(int seat) => _hasActedSinceLastRaise.Add(seat);

    private void Advance()
    {
        if (_active.Count == 0) return;
        var idx = _active.IndexOf(CurrentSeat);
        if (idx < 0) idx = -1;
        for (var step = 1; step <= _active.Count; step++)
        {
            var next = _active[(idx + step) % _active.Count];
            if (!_hasActedSinceLastRaise.Contains(next) || step == _active.Count)
            {
                CurrentSeat = next;
                return;
            }
        }
    }
}
