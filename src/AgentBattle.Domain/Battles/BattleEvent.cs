using System.Text.Json.Serialization;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Domain.Battles;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(BattleStarted), "battle_started")]
[JsonDerivedType(typeof(HandStarted), "hand_started")]
[JsonDerivedType(typeof(HoleCardsDealt), "hole_cards_dealt")]
[JsonDerivedType(typeof(CommunityDealt), "community_dealt")]
[JsonDerivedType(typeof(AgentTurnStarted), "agent_turn_started")]
[JsonDerivedType(typeof(AgentThoughts), "agent_thoughts")]
[JsonDerivedType(typeof(AgentAction), "agent_action")]
[JsonDerivedType(typeof(AgentActionRejected), "agent_action_rejected")]
[JsonDerivedType(typeof(Showdown), "showdown")]
[JsonDerivedType(typeof(HandEnded), "hand_ended")]
[JsonDerivedType(typeof(BattleEnded), "battle_ended")]
public abstract record BattleEvent(System.DateTimeOffset Ts)
{
    public sealed record BattleStarted(System.DateTimeOffset Ts, string BattleId, string ConfigSnapshot, IReadOnlyList<SeatedAgent> Agents) : BattleEvent(Ts);
    public sealed record HandStarted(System.DateTimeOffset Ts, int HandNo, int ButtonSeat, int SbSeat, int BbSeat, IReadOnlyList<int> InactiveSeats) : BattleEvent(Ts);
    public sealed record HoleCardsDealt(System.DateTimeOffset Ts, int HandNo, IReadOnlyList<HoleCardDeal> Deals) : BattleEvent(Ts);
    public sealed record CommunityDealt(System.DateTimeOffset Ts, int HandNo, Street Street, IReadOnlyList<Card> Cards) : BattleEvent(Ts);
    public sealed record AgentTurnStarted(System.DateTimeOffset Ts, int HandNo, int Seat, PokerState StateSnapshot) : BattleEvent(Ts);
    public sealed record AgentThoughts(System.DateTimeOffset Ts, int HandNo, int Seat, string Text, int Tokens, int Attempt) : BattleEvent(Ts);
    public sealed record AgentAction(System.DateTimeOffset Ts, int HandNo, int Seat, string Action, int? Amount, int Attempt, string? AutoReason) : BattleEvent(Ts);
    public sealed record AgentActionRejected(System.DateTimeOffset Ts, int HandNo, int Seat, string Action, int? Amount, string Reason, int Attempt) : BattleEvent(Ts);
    public sealed record Showdown(System.DateTimeOffset Ts, int HandNo, IReadOnlyList<HoleCardDeal> Reveals, IReadOnlyList<PotWinner> Winners) : BattleEvent(Ts);
    public sealed record HandEnded(System.DateTimeOffset Ts, int HandNo, IReadOnlyDictionary<int, int> Stacks) : BattleEvent(Ts);
    public sealed record BattleEnded(System.DateTimeOffset Ts, IReadOnlyDictionary<int, int> FinalStacks, IReadOnlyList<RankEntry> Ranking) : BattleEvent(Ts);
}

public sealed record SeatedAgent(int Seat, string Id, string DisplayName);
public sealed record HoleCardDeal(int Seat, IReadOnlyList<Card> Cards);
public sealed record PotWinner(int Seat, int Pot, string HandDescription);
public sealed record RankEntry(int Seat, int Chips, string AgentId);
