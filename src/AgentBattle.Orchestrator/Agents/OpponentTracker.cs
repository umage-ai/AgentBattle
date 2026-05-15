using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

/// <summary>
/// Cross-hand statistics + showdown memory for every seat at the table.
/// Fed by the orchestrator as actions and showdowns are recorded, then rendered
/// into the per-turn prompt so each agent can reason about opponents.
/// </summary>
public sealed class OpponentTracker
{
    private readonly Dictionary<int, SeatStats> _stats = new();
    private readonly List<ShowdownRecord> _showdowns = new();

    public OpponentTracker(IEnumerable<int> seats)
    {
        foreach (var s in seats) _stats[s] = new SeatStats();
    }

    public void OnAction(int handNo, int seat, Street street, string action, int? amount, int toCallBefore)
    {
        if (!_stats.TryGetValue(seat, out var st)) return;

        if (street == Street.Preflop)
        {
            // First voluntary action this hand on preflop?
            if (!st.PreflopActedHands.Contains(handNo))
            {
                st.PreflopActedHands.Add(handNo);
                st.PreflopOpportunities++;
                // VPIP = call (non-zero) or raise on first preflop touch.
                if (action == "call" || action == "raise" || action == "all_in")
                    st.VpipCount++;
                if (action == "raise" || action == "all_in")
                    st.PfrCount++;
            }
        }

        // Aggression: raise/all_in counts as aggressive; call as passive.
        if (action == "raise" || action == "all_in") st.AggressiveActions++;
        if (action == "call") st.PassiveActions++;

        // Fold-to-aggression: a fold facing a non-zero to_call counts.
        if (action == "fold" && toCallBefore > 0)
        {
            st.FoldsToBet++;
            st.FacedBets++;
        }
        else if (toCallBefore > 0 && (action == "call" || action == "raise" || action == "all_in"))
        {
            st.FacedBets++;
        }

        st.RecentActions.Add(new RecentAction(handNo, street, action, amount));
        if (st.RecentActions.Count > 12) st.RecentActions.RemoveAt(0);
    }

    public void OnShowdown(int handNo, IReadOnlyList<int> communityRanks, IReadOnlyList<Card> community,
                            IReadOnlyDictionary<int, IReadOnlyList<Card>> reveals,
                            IReadOnlyList<int> winners)
    {
        foreach (var (seat, hole) in reveals)
        {
            if (_stats.TryGetValue(seat, out var st))
            {
                st.Showdowns++;
                if (winners.Contains(seat)) st.ShowdownsWon++;
            }
        }
        _showdowns.Add(new ShowdownRecord(handNo, community, reveals, winners));
        if (_showdowns.Count > 8) _showdowns.RemoveAt(0);
    }

    public IReadOnlyList<int> Seats => _stats.Keys.ToArray();

    public SeatStats Get(int seat) => _stats[seat];

    public IReadOnlyList<ShowdownRecord> RecentShowdowns => _showdowns;

    public sealed class SeatStats
    {
        public int PreflopOpportunities { get; set; }
        public int VpipCount { get; set; }
        public int PfrCount { get; set; }
        public int AggressiveActions { get; set; }
        public int PassiveActions { get; set; }
        public int FacedBets { get; set; }
        public int FoldsToBet { get; set; }
        public int Showdowns { get; set; }
        public int ShowdownsWon { get; set; }
        public HashSet<int> PreflopActedHands { get; } = new();
        public List<RecentAction> RecentActions { get; } = new();

        public int? VpipPct => PreflopOpportunities == 0 ? null : (int)Math.Round(100.0 * VpipCount / PreflopOpportunities);
        public int? PfrPct => PreflopOpportunities == 0 ? null : (int)Math.Round(100.0 * PfrCount / PreflopOpportunities);
        public double? AggressionFactor => PassiveActions == 0
            ? (AggressiveActions > 0 ? (double?)AggressiveActions : null)
            : AggressiveActions / (double)PassiveActions;
        public int? FoldToBetPct => FacedBets == 0 ? null : (int)Math.Round(100.0 * FoldsToBet / FacedBets);
    }

    public sealed record RecentAction(int HandNo, Street Street, string Action, int? Amount);
    public sealed record ShowdownRecord(int HandNo, IReadOnlyList<Card> Community,
                                        IReadOnlyDictionary<int, IReadOnlyList<Card>> Reveals,
                                        IReadOnlyList<int> Winners);
}
