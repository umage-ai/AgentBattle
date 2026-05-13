using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Poker.Mcp.Engine;

public sealed record ApplyResult(bool Ok, string? Reason, PokerAction? Applied)
{
    public static ApplyResult Rejected(string reason) => new(false, reason, null);
    public static ApplyResult OkApplied(PokerAction a) => new(true, null, a);
}

public sealed record ShowdownResult(
    IReadOnlyDictionary<int, int> Stacks,
    IReadOnlyList<(int Seat, int Pot, string Description)> Winners,
    IReadOnlyDictionary<int, IReadOnlyList<Card>> Reveals);

public sealed class PokerGame
{
    private readonly IReadOnlyList<int> _seats;
    private readonly Dictionary<int, int> _stacks;
    private readonly Dictionary<int, string> _agentNames;
    private readonly int _sb;
    private readonly int _bb;
    private readonly int _deckSeed;

    private int _button;
    private bool _buttonInitialised;
    private int _handNo;

    private Deck _deck = new(0);
    private readonly List<Card> _community = [];
    private readonly Dictionary<int, List<Card>> _hole = [];
    private readonly Dictionary<int, int> _streetBets = [];
    private readonly Dictionary<int, int> _handContributions = [];
    private readonly HashSet<int> _folded = [];
    private readonly HashSet<int> _allIn = [];
    private readonly List<ActionLogEntry> _log = [];
    private Street _street = Street.Preflop;
    private BettingRound _round = new([], 0, 0, 0);

    public PokerGame(
        IReadOnlyList<int> seats,
        Dictionary<int, int> startingStacks,
        Dictionary<int, string> agentNames,
        int smallBlind, int bigBlind,
        int buttonSeat, int deckSeed)
    {
        _seats = seats.ToArray();
        _stacks = new Dictionary<int, int>(startingStacks);
        _agentNames = new Dictionary<int, string>(agentNames);
        _sb = smallBlind;
        _bb = bigBlind;
        _button = buttonSeat;
        _buttonInitialised = false;
        _deckSeed = deckSeed;
    }

    public int CurrentSeat => _round.CurrentSeat;
    public Street Street => _street;
    public int HandNo => _handNo;

    public IReadOnlyList<int> ActiveSeats() =>
        _seats.Where(s => _stacks.GetValueOrDefault(s, 0) > 0 || _handContributions.GetValueOrDefault(s, 0) > 0)
              .Where(s => !_folded.Contains(s))
              .ToArray();

    public IReadOnlyDictionary<int, IReadOnlyList<Card>> CurrentHoleCards() =>
        _hole.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Card>)kv.Value);

    public PokerState GetMyState(int seat)
    {
        var legal = ComputeLegal(seat);
        var pot = _handContributions.Values.Sum();
        var seatSummaries = _seats.Select(s => new SeatSummary(
            Seat: s,
            AgentDisplayName: _agentNames.GetValueOrDefault(s, $"seat{s}"),
            Stack: _stacks.GetValueOrDefault(s, 0),
            CurrentBet: _streetBets.GetValueOrDefault(s, 0),
            HasFolded: _folded.Contains(s),
            IsAllIn: _allIn.Contains(s),
            IsInactive: _stacks.GetValueOrDefault(s, 0) == 0 && _handContributions.GetValueOrDefault(s, 0) == 0))
            .ToArray();

        var hole = _hole.TryGetValue(seat, out var h)
            ? (IReadOnlyList<Card>)h.ToArray()
            : Array.Empty<Card>();

        return new PokerState(
            HandNo: _handNo,
            Street: _street,
            Seat: seat,
            HoleCards: hole,
            Community: _community.ToArray(),
            MyStack: _stacks.GetValueOrDefault(seat, 0),
            MyCurrentBet: _streetBets.GetValueOrDefault(seat, 0),
            Pot: pot,
            ToCall: legal.CallAmount,
            Seats: seatSummaries,
            ActionLog: _log.ToArray(),
            CurrentSeat: _round.CurrentSeat,
            Legal: legal);
    }

    public void StartHand()
    {
        _handNo++;

        _community.Clear();
        _hole.Clear();
        _streetBets.Clear();
        _handContributions.Clear();
        _folded.Clear();
        _allIn.Clear();
        _log.Clear();
        _street = Street.Preflop;

        _deck = new Deck(_deckSeed + _handNo);

        // Active = seats with chips.
        var active = _seats.Where(s => _stacks.GetValueOrDefault(s, 0) > 0).ToArray();
        if (active.Length < 2)
            throw new InvalidOperationException("Need at least 2 players with chips to start a hand.");

        // Rotate button: keep the constructor seat for hand 1, advance on subsequent hands.
        if (!_buttonInitialised)
        {
            // Ensure the initial button is on an active seat; if not, advance to the next active.
            if (!active.Contains(_button))
                _button = NextSeatAfter(_button, active);
            _buttonInitialised = true;
        }
        else
        {
            _button = NextSeatAfter(_button, active);
        }

        int sbSeat, bbSeat, startSeat;
        if (active.Length == 2)
        {
            // Heads-up: button is SB and acts first preflop.
            sbSeat = _button;
            bbSeat = active.First(s => s != _button);
            startSeat = sbSeat;
        }
        else
        {
            sbSeat = NextSeatAfter(_button, active);
            bbSeat = NextSeatAfter(sbSeat, active);
            startSeat = NextSeatAfter(bbSeat, active); // UTG
        }

        // Post blinds (clamped to stack defensively).
        PostBlind(sbSeat, _sb);
        PostBlind(bbSeat, _bb);

        // Deal two hole cards per active seat (round-robin starting left of button).
        // The order is: dealing begins to seat left of button. We'll iterate the
        // active seats starting at NextSeatAfter(_button, active) for stable order.
        var dealOrder = new List<int>();
        var dealStart = active.Length == 2 ? bbSeat : NextSeatAfter(_button, active);
        var cur = dealStart;
        for (var i = 0; i < active.Length; i++)
        {
            dealOrder.Add(cur);
            cur = NextSeatAfter(cur, active);
        }
        for (var card = 0; card < 2; card++)
        {
            foreach (var s in dealOrder)
            {
                if (!_hole.ContainsKey(s)) _hole[s] = [];
                _hole[s].Add(_deck.Draw());
            }
        }

        // Open preflop round. BettingRound.ActiveSeats == all active seats; BB hasn't
        // been "acted" yet and gets their option when action returns.
        _round = new BettingRound(active, startSeat, currentBet: _bb, minRaise: _bb);

        // If posting the BB left only one non-folded seat (shouldn't with stacks > 0),
        // bail straight to showdown.
        AdvanceStreetIfRoundClosed();
    }

    private void PostBlind(int seat, int amount)
    {
        var stack = _stacks.GetValueOrDefault(seat, 0);
        var actual = System.Math.Min(stack, amount);
        ContributeChips(seat, actual);
        if (_stacks[seat] == 0) _allIn.Add(seat);
        _log.Add(new ActionLogEntry(seat, "post", actual, Street.Preflop));
    }

    public ApplyResult Apply(PokerAction action)
    {
        if (_street == Street.Showdown)
            return ApplyResult.Rejected("Hand is at showdown.");
        if (action.Seat != _round.CurrentSeat)
            return ApplyResult.Rejected($"Not seat {action.Seat}'s turn (current: {_round.CurrentSeat}).");

        var legal = ComputeLegal(action.Seat);

        switch (action)
        {
            case PokerAction.Fold:
                if (!legal.CanFold) return ApplyResult.Rejected("Fold not legal.");
                _folded.Add(action.Seat);
                _round.RecordFold(action.Seat);
                _log.Add(new ActionLogEntry(action.Seat, "fold", null, _street));
                AdvanceStreetIfRoundClosed();
                return ApplyResult.OkApplied(action);

            case PokerAction.Check:
                if (!legal.CanCheck) return ApplyResult.Rejected("Check not legal.");
                _round.RecordCheck(action.Seat);
                _log.Add(new ActionLogEntry(action.Seat, "check", null, _street));
                AdvanceStreetIfRoundClosed();
                return ApplyResult.OkApplied(action);

            case PokerAction.Call:
            {
                if (!legal.CanCall) return ApplyResult.Rejected("Call not legal.");
                var amount = legal.CallAmount;
                ContributeChips(action.Seat, amount);
                if (_stacks[action.Seat] == 0) _allIn.Add(action.Seat);
                _round.RecordCall(action.Seat, amount);
                _log.Add(new ActionLogEntry(action.Seat, "call", amount, _street));
                AdvanceStreetIfRoundClosed();
                return ApplyResult.OkApplied(action);
            }

            case PokerAction.Raise raise:
            {
                if (!legal.CanRaise) return ApplyResult.Rejected("Raise not legal.");
                if (raise.Amount < legal.MinRaiseTotal)
                    return ApplyResult.Rejected($"Raise below minimum ({legal.MinRaiseTotal}).");
                if (raise.Amount > legal.MaxRaiseTotal)
                    return ApplyResult.Rejected($"Raise exceeds maximum ({legal.MaxRaiseTotal}).");
                var myBet = _streetBets.GetValueOrDefault(action.Seat, 0);
                var delta = raise.Amount - myBet;
                ContributeChips(action.Seat, delta);
                if (_stacks[action.Seat] == 0) _allIn.Add(action.Seat);
                _round.RecordRaiseTo(action.Seat, raise.Amount);
                _log.Add(new ActionLogEntry(action.Seat, "raise", raise.Amount, _street));
                AdvanceStreetIfRoundClosed();
                return ApplyResult.OkApplied(action);
            }

            case PokerAction.AllIn:
            {
                var myBet = _streetBets.GetValueOrDefault(action.Seat, 0);
                var stack = _stacks.GetValueOrDefault(action.Seat, 0);
                var totalBet = myBet + stack;
                // Treat as a raise to totalBet; if it's below min raise we still allow it as an all-in call+short raise.
                if (totalBet <= _round.CurrentBet)
                {
                    // All-in for less than current bet → effectively a call (short).
                    var callAmount = stack;
                    ContributeChips(action.Seat, callAmount);
                    _allIn.Add(action.Seat);
                    _round.RecordCall(action.Seat, callAmount);
                    _log.Add(new ActionLogEntry(action.Seat, "allin", callAmount, _street));
                }
                else
                {
                    ContributeChips(action.Seat, stack);
                    _allIn.Add(action.Seat);
                    _round.RecordRaiseTo(action.Seat, totalBet);
                    _log.Add(new ActionLogEntry(action.Seat, "allin", totalBet, _street));
                }
                AdvanceStreetIfRoundClosed();
                return ApplyResult.OkApplied(action);
            }

            default:
                return ApplyResult.Rejected($"Unknown action: {action.GetType().Name}");
        }
    }

    private void ContributeChips(int seat, int amount)
    {
        _stacks[seat] -= amount;
        _streetBets[seat] = _streetBets.GetValueOrDefault(seat, 0) + amount;
        _handContributions[seat] = _handContributions.GetValueOrDefault(seat, 0) + amount;
    }

    private LegalActions ComputeLegal(int seat)
    {
        var currentBet = _round.CurrentBet;
        var myBet = _streetBets.GetValueOrDefault(seat, 0);
        var toCall = System.Math.Max(0, currentBet - myBet);
        var stack = _stacks.GetValueOrDefault(seat, 0);

        var canCheck = toCall == 0;
        var canCall = toCall > 0 && stack > 0;
        var callAmount = System.Math.Min(toCall, stack);
        var minRaiseTotal = currentBet + _round.MinRaise;
        var maxRaiseTotal = myBet + stack;
        var canRaise = stack > toCall && maxRaiseTotal >= minRaiseTotal;
        return new LegalActions(canCheck, canCall, callAmount, canRaise, minRaiseTotal, maxRaiseTotal, CanFold: !canCheck);
    }

    private void AdvanceStreetIfRoundClosed()
    {
        // If only one non-folded seat remains, jump to showdown.
        var notFolded = _seats.Where(s => _handContributions.GetValueOrDefault(s, 0) > 0 || _stacks.GetValueOrDefault(s, 0) > 0)
                              .Where(s => !_folded.Contains(s))
                              .ToArray();
        if (notFolded.Length <= 1)
        {
            _street = Street.Showdown;
            return;
        }

        if (!_round.IsClosed) return;

        // Round closed — advance street.
        _streetBets.Clear();

        switch (_street)
        {
            case Street.Preflop:
                _street = Street.Flop;
                _deck.Draw(); // burn
                _community.Add(_deck.Draw());
                _community.Add(_deck.Draw());
                _community.Add(_deck.Draw());
                OpenNewRound();
                break;
            case Street.Flop:
                _street = Street.Turn;
                _deck.Draw(); // burn
                _community.Add(_deck.Draw());
                OpenNewRound();
                break;
            case Street.Turn:
                _street = Street.River;
                _deck.Draw(); // burn
                _community.Add(_deck.Draw());
                OpenNewRound();
                break;
            case Street.River:
                _street = Street.Showdown;
                break;
        }

        // If after opening the new round everyone capable of acting is all-in, skip
        // straight through remaining streets to showdown (no further action possible).
        while (_street != Street.Showdown)
        {
            var active = _seats.Where(s => !_folded.Contains(s))
                               .Where(s => !_allIn.Contains(s))
                               .Where(s => _stacks.GetValueOrDefault(s, 0) > 0)
                               .ToArray();
            if (active.Length >= 2) break;
            // <= 1 seat able to act — auto-deal remaining streets and showdown.
            switch (_street)
            {
                case Street.Flop:
                    _street = Street.Turn;
                    _deck.Draw();
                    _community.Add(_deck.Draw());
                    break;
                case Street.Turn:
                    _street = Street.River;
                    _deck.Draw();
                    _community.Add(_deck.Draw());
                    break;
                case Street.River:
                    _street = Street.Showdown;
                    break;
            }
        }
    }

    private void OpenNewRound()
    {
        var active = _seats.Where(s => !_folded.Contains(s))
                           .Where(s => !_allIn.Contains(s))
                           .Where(s => _stacks.GetValueOrDefault(s, 0) > 0)
                           .ToArray();
        if (active.Length == 0)
        {
            _round = new BettingRound(active, 0, 0, _bb);
            return;
        }
        // Post-flop: action starts left of button (first non-folded, non-all-in seat).
        var start = NextSeatAfter(_button, active);
        _round = new BettingRound(active, start, currentBet: 0, minRaise: _bb);
    }

    public ShowdownResult ResolveShowdown()
    {
        var pots = PotManager.BuildPots(new Dictionary<int, int>(_handContributions), _folded.ToHashSet());
        var winners = new List<(int Seat, int Pot, string Description)>();
        foreach (var pot in pots)
        {
            if (pot.EligibleSeats.Count == 0) continue;
            if (pot.EligibleSeats.Count == 1)
            {
                _stacks[pot.EligibleSeats[0]] += pot.Amount;
                winners.Add((pot.EligibleSeats[0], pot.Amount, "uncontested"));
                continue;
            }
            var scored = pot.EligibleSeats
                .Select(s => (Seat: s, Rank: HandEvaluator.Evaluate([.. _hole[s], .. _community])))
                .ToArray();
            var best = scored.Aggregate((a, b) => a.Rank.CompareTo(b.Rank) >= 0 ? a : b);
            var topGroup = scored.Where(s => s.Rank.CompareTo(best.Rank) == 0).ToArray();
            var share = pot.Amount / topGroup.Length;
            var remainder = pot.Amount - share * topGroup.Length;
            foreach (var w in topGroup)
            {
                _stacks[w.Seat] += share;
                winners.Add((w.Seat, share, w.Rank.Description));
            }
            if (remainder > 0) _stacks[topGroup[0].Seat] += remainder;
        }
        var reveals = (IReadOnlyDictionary<int, IReadOnlyList<Card>>)_hole
            .Where(kv => !_folded.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Card>)kv.Value);
        return new ShowdownResult(new Dictionary<int, int>(_stacks), winners, reveals);
    }

    private int NextSeatAfter(int reference, IReadOnlyList<int> eligible)
    {
        // Find reference's position in the master _seats list, then walk forward.
        var refIdx = -1;
        for (var i = 0; i < _seats.Count; i++)
            if (_seats[i] == reference) { refIdx = i; break; }
        for (var step = 1; step <= _seats.Count; step++)
        {
            var candidate = _seats[(refIdx + step) % _seats.Count];
            if (eligible.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException($"No eligible seat after {reference}");
    }
}
