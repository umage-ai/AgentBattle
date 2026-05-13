using System.Text.Json;
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Json;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Domain.Tests.Battles;

public class BattleEventJsonTests
{
    private readonly JsonSerializerOptions _opts = BattleEventJsonOptions.Default;

    [Fact]
    public void BattleStarted_round_trips()
    {
        BattleEvent e = new BattleEvent.BattleStarted(
            Ts: System.DateTimeOffset.UnixEpoch,
            BattleId: "abc123",
            ConfigSnapshot: """{"hands":50}""",
            Agents: [new SeatedAgent(0, "gpt-5", "GPT-5"), new SeatedAgent(1, "opus", "Opus")]);

        var json = JsonSerializer.Serialize(e, _opts);
        json.Should().Contain("\"t\":\"battle_started\"");
        var back = JsonSerializer.Deserialize<BattleEvent>(json, _opts);
        back.Should().BeOfType<BattleEvent.BattleStarted>()
            .Which.BattleId.Should().Be("abc123");
    }

    [Fact]
    public void AgentAction_serializes_with_optional_amount_and_auto_reason()
    {
        BattleEvent e = new BattleEvent.AgentAction(
            Ts: System.DateTimeOffset.UnixEpoch,
            HandNo: 1, Seat: 3, Action: "raise", Amount: 60, Attempt: 1, AutoReason: null);
        var json = JsonSerializer.Serialize(e, _opts);
        json.Should().Contain("\"action\":\"raise\"")
            .And.Contain("\"amount\":60")
            .And.NotContain("auto_reason");
    }

    [Fact]
    public void HoleCardsDealt_carries_full_reveal_for_god_view()
    {
        BattleEvent e = new BattleEvent.HoleCardsDealt(
            Ts: System.DateTimeOffset.UnixEpoch,
            HandNo: 1,
            Deals: [new HoleCardDeal(0, [Card.Parse("As"), Card.Parse("Kd")])]);
        var json = JsonSerializer.Serialize(e, _opts);
        json.Should().Contain("\"As\"").And.Contain("\"Kd\"");
    }
}
