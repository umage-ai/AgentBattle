# AgentBattle Poker MVP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first vertical slice of AgentBattle — a CLI that runs a recorded Texas Hold'em 6-max match between up to six AI agents (any OpenAI-compatible endpoint) and a web viewer that plays back the recording with each agent's reasoning visible per turn.

**Architecture:** Three layers built bottom-up. L1 is a pure Texas Hold'em engine wrapped as an MCP server. L2 is an orchestrator that spawns the MCP server, drives per-agent OpenAI chat sessions, validates and forwards tool calls, and appends a JSONL event stream. L3 is a Razor Pages web app that reads the JSONL stream and renders a replay UI with HTMX + Alpine.js.

**Tech Stack:** .NET 10, C#, ASP.NET Core Razor Pages, HTMX, Alpine.js, `ModelContextProtocol` (official C# MCP SDK), `YamlDotNet`, `System.CommandLine`, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-05-13-agentbattle-poker-mvp-design.md`

## Milestone map

| Milestone | Layer | Done when |
|---|---|---|
| M1 | Solution scaffold | `dotnet build` succeeds against the empty solution and CI-style check passes |
| M2 | L1 — Domain + Poker engine + MCP server | `mcp-cli` (or hand-rolled stdio client) can play a full hand against the server by exchanging tool calls |
| M3 | L2 — Orchestrator + Battle runner CLI | `battle run --config configs/sample.yaml` against a stub agent server produces a complete JSONL file ending in `battle_ended` |
| M4 | L2 — Real agent integration | The CLI runs end-to-end against at least one real OpenAI-compatible endpoint (e.g. local Ollama) |
| M5 | L3 — Web viewer (battle list + replay) | `dotnet run --project AgentBattle.Web` serves a list page and a replay page that plays back a real JSONL file with thoughts visible |
| M6 | L3 — Polish | God-view toggle works, agents page lists profiles, animations look right |

---

## File structure (target end state)

```
AgentBattle/
  AgentBattle.sln
  Directory.Build.props          # shared <LangVersion>, <Nullable>, <TreatWarningsAsErrors>
  Directory.Packages.props       # central package versioning
  .editorconfig
  .gitignore
  README.md
  src/
    AgentBattle.Domain/
      AgentBattle.Domain.csproj
      Cards/{Rank.cs, Suit.cs, Card.cs}
      Poker/{PokerAction.cs, PokerState.cs, LegalActions.cs, Street.cs}
      Battles/{AgentProfile.cs, BattleConfig.cs, BattleEvent.cs}
      Json/BattleEventJsonOptions.cs
    AgentBattle.Poker.Mcp/
      AgentBattle.Poker.Mcp.csproj
      Program.cs
      Engine/{Deck.cs, HandEvaluator.cs, BettingRound.cs, PotManager.cs, PokerGame.cs}
      Mcp/{StateProjection.cs, PokerTools.cs}
    AgentBattle.Orchestrator/
      AgentBattle.Orchestrator.csproj
      BattleOrchestrator.cs
      Mcp/McpGameClient.cs
      Agents/{IAgentClient.cs, OpenAiCompatibleAgent.cs, AgentSession.cs, PromptBuilder.cs, ActionParser.cs}
      Recording/{IBattleEventSink.cs, JsonlEventSink.cs}
      TurnLoop/TurnRunner.cs
    AgentBattle.BattleRunner/
      AgentBattle.BattleRunner.csproj
      Program.cs
      Config/ConfigLoader.cs
    AgentBattle.Web/
      AgentBattle.Web.csproj
      Program.cs
      appsettings.json
      Pages/{Index.cshtml(.cs), Battles/Replay.cshtml(.cs), Agents/Index.cshtml(.cs)}
      Services/{BattleArchive.cs, AgentRegistry.cs}
      wwwroot/{css/site.css, js/replay.js, js/poker-table.js, lib/htmx.min.js, lib/alpine.min.js}
  tests/
    AgentBattle.Domain.Tests/AgentBattle.Domain.Tests.csproj
    AgentBattle.Poker.Mcp.Tests/AgentBattle.Poker.Mcp.Tests.csproj
    AgentBattle.Orchestrator.Tests/AgentBattle.Orchestrator.Tests.csproj
    AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj
  agents/                           # YAML agent profiles (sample fixtures committed)
  configs/                          # YAML battle configs (sample fixtures committed)
  battles/                          # JSONL outputs (gitignored except .gitkeep + fixtures)
  fixtures/                         # Sample JSONL files for web tests + local development
```

---

# Milestone 1 — Solution scaffold

### Task 1.1: Initialize the .NET solution

**Files:**
- Create: `AgentBattle.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `README.md`

- [ ] **Step 1: Initialize git and create the solution**

Run from `C:\Projects\AgentBattle`:

```pwsh
git init
dotnet new sln -n AgentBattle
```

- [ ] **Step 2: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Pin exact versions at first restore. Update here, never in csproj files. -->
    <PackageVersion Include="ModelContextProtocol" Version="*" />
    <PackageVersion Include="YamlDotNet" Version="*" />
    <PackageVersion Include="System.CommandLine" Version="*" />
    <PackageVersion Include="xunit" Version="*" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="*" />
    <PackageVersion Include="FluentAssertions" Version="*" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="*" />
  </ItemGroup>
</Project>
```

When restoring the first time, replace `*` with the resolved versions to lock them. Document in README that updates go through this file only.

- [ ] **Step 4: Create `.gitignore`** — standard Visual Studio template plus:

```
bin/
obj/
*.user
.vs/
battles/*.jsonl
!battles/.gitkeep
.env
.env.*
!.env.example
```

- [ ] **Step 5: Create `.editorconfig`** — standard C# defaults (4-space indent, CRLF, file-scoped namespaces enforced).

- [ ] **Step 6: Create `README.md`** with these sections (one-paragraph each):

```markdown
# AgentBattle

Record and replay battles between AI agents in turn-based games. MVP plays Texas
Hold'em 6-max between any OpenAI-compatible endpoints (cloud or local).

## Quick start

# Coming once the runner exists — leave as TODO that the BattleRunner task replaces.

## Layout

(reference the file structure section in the plan/spec)

## Docs

- Design spec: `docs/superpowers/specs/2026-05-13-agentbattle-poker-mvp-design.md`
- Implementation plan: `docs/superpowers/plans/2026-05-13-agentbattle-poker-mvp.md`
```

- [ ] **Step 7: Create `battles/.gitkeep`** so the empty directory is committed.

- [ ] **Step 8: Verify build**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (nothing to build, exits clean)

- [ ] **Step 9: Commit**

```pwsh
git add -A
git commit -m "chore: initialize AgentBattle solution scaffold"
```

---

# Milestone 2 — Domain types, poker engine, MCP server

### Task 2.1: Create the Domain class library and card primitives

**Files:**
- Create: `src/AgentBattle.Domain/AgentBattle.Domain.csproj`
- Create: `src/AgentBattle.Domain/Cards/Rank.cs`
- Create: `src/AgentBattle.Domain/Cards/Suit.cs`
- Create: `src/AgentBattle.Domain/Cards/Card.cs`
- Create: `tests/AgentBattle.Domain.Tests/AgentBattle.Domain.Tests.csproj`
- Create: `tests/AgentBattle.Domain.Tests/Cards/CardTests.cs`

- [ ] **Step 1: Create the projects and add to solution**

```pwsh
dotnet new classlib -n AgentBattle.Domain -o src/AgentBattle.Domain
dotnet new xunit -n AgentBattle.Domain.Tests -o tests/AgentBattle.Domain.Tests
dotnet sln add src/AgentBattle.Domain/AgentBattle.Domain.csproj
dotnet sln add tests/AgentBattle.Domain.Tests/AgentBattle.Domain.Tests.csproj
dotnet add tests/AgentBattle.Domain.Tests reference src/AgentBattle.Domain
dotnet add tests/AgentBattle.Domain.Tests package FluentAssertions
```

Delete the auto-generated `Class1.cs` from both projects.

- [ ] **Step 2: Write failing test for Card parsing**

Create `tests/AgentBattle.Domain.Tests/Cards/CardTests.cs`:

```csharp
using AgentBattle.Domain.Cards;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Domain.Tests.Cards;

public class CardTests
{
    [Theory]
    [InlineData("As", Rank.Ace, Suit.Spades)]
    [InlineData("Td", Rank.Ten, Suit.Diamonds)]
    [InlineData("2c", Rank.Two, Suit.Clubs)]
    [InlineData("Kh", Rank.King, Suit.Hearts)]
    public void Parse_returns_card_for_two_char_notation(string input, Rank rank, Suit suit)
    {
        var card = Card.Parse(input);
        card.Rank.Should().Be(rank);
        card.Suit.Should().Be(suit);
    }

    [Fact]
    public void ToString_round_trips_through_Parse()
    {
        foreach (var rank in System.Enum.GetValues<Rank>())
        foreach (var suit in System.Enum.GetValues<Suit>())
        {
            var card = new Card(rank, suit);
            Card.Parse(card.ToString()).Should().Be(card);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Asd")]
    [InlineData("1s")]
    [InlineData("Az")]
    public void Parse_throws_for_invalid_notation(string input)
    {
        var act = () => Card.Parse(input);
        act.Should().Throw<System.FormatException>();
    }
}
```

- [ ] **Step 3: Run test, confirm fail**

Run: `dotnet test tests/AgentBattle.Domain.Tests`
Expected: FAIL — types `Rank`, `Suit`, `Card` do not exist.

- [ ] **Step 4: Implement `Rank.cs`**

```csharp
namespace AgentBattle.Domain.Cards;

public enum Rank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
    Jack, Queen, King, Ace
}
```

- [ ] **Step 5: Implement `Suit.cs`**

```csharp
namespace AgentBattle.Domain.Cards;

public enum Suit { Clubs, Diamonds, Hearts, Spades }
```

- [ ] **Step 6: Implement `Card.cs`**

```csharp
namespace AgentBattle.Domain.Cards;

public readonly record struct Card(Rank Rank, Suit Suit)
{
    public static Card Parse(string s)
    {
        if (s is not { Length: 2 })
            throw new System.FormatException($"Card notation must be 2 chars: '{s}'");
        var rank = s[0] switch
        {
            '2' => Rank.Two, '3' => Rank.Three, '4' => Rank.Four, '5' => Rank.Five,
            '6' => Rank.Six, '7' => Rank.Seven, '8' => Rank.Eight, '9' => Rank.Nine,
            'T' or 't' => Rank.Ten, 'J' or 'j' => Rank.Jack, 'Q' or 'q' => Rank.Queen,
            'K' or 'k' => Rank.King, 'A' or 'a' => Rank.Ace,
            _ => throw new System.FormatException($"Invalid rank char: '{s[0]}'")
        };
        var suit = s[1] switch
        {
            'c' or 'C' => Suit.Clubs, 'd' or 'D' => Suit.Diamonds,
            'h' or 'H' => Suit.Hearts, 's' or 'S' => Suit.Spades,
            _ => throw new System.FormatException($"Invalid suit char: '{s[1]}'")
        };
        return new Card(rank, suit);
    }

    public override string ToString()
    {
        var r = Rank switch
        {
            Rank.Ten => 'T', Rank.Jack => 'J', Rank.Queen => 'Q',
            Rank.King => 'K', Rank.Ace => 'A',
            _ => (char)('0' + (int)Rank)
        };
        var s = Suit switch
        {
            Suit.Clubs => 'c', Suit.Diamonds => 'd',
            Suit.Hearts => 'h', Suit.Spades => 's',
            _ => '?'
        };
        return $"{r}{s}";
    }
}
```

- [ ] **Step 7: Run tests, confirm pass**

Run: `dotnet test tests/AgentBattle.Domain.Tests`
Expected: PASS — all CardTests green.

- [ ] **Step 8: Commit**

```pwsh
git add -A
git commit -m "feat(domain): add Card, Rank, Suit primitives with notation parsing"
```

### Task 2.2: Domain types for poker actions, state, and legal-actions

**Files:**
- Create: `src/AgentBattle.Domain/Poker/Street.cs`
- Create: `src/AgentBattle.Domain/Poker/PokerAction.cs`
- Create: `src/AgentBattle.Domain/Poker/LegalActions.cs`
- Create: `src/AgentBattle.Domain/Poker/PokerState.cs`
- Create: `tests/AgentBattle.Domain.Tests/Poker/PokerActionTests.cs`

- [ ] **Step 1: Write failing test for action types**

Create `tests/AgentBattle.Domain.Tests/Poker/PokerActionTests.cs`:

```csharp
using AgentBattle.Domain.Poker;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Domain.Tests.Poker;

public class PokerActionTests
{
    [Fact]
    public void Fold_action_carries_seat_and_no_amount()
    {
        PokerAction action = new PokerAction.Fold(Seat: 3);
        action.Seat.Should().Be(3);
        action.Should().BeOfType<PokerAction.Fold>();
    }

    [Fact]
    public void Raise_action_carries_amount_as_total_bet_level()
    {
        PokerAction action = new PokerAction.Raise(Seat: 2, Amount: 60);
        action.Should().BeOfType<PokerAction.Raise>()
            .Which.Amount.Should().Be(60);
    }

    [Fact]
    public void LegalActions_describes_what_a_seat_can_do()
    {
        var legal = new LegalActions(
            CanCheck: false, CanCall: true, CallAmount: 40,
            CanRaise: true, MinRaiseTotal: 80, MaxRaiseTotal: 500,
            CanFold: true);
        legal.CanCheck.Should().BeFalse();
        legal.CallAmount.Should().Be(40);
        legal.MinRaiseTotal.Should().Be(80);
    }
}
```

- [ ] **Step 2: Run, confirm fail**

Run: `dotnet test tests/AgentBattle.Domain.Tests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement `Street.cs`**

```csharp
namespace AgentBattle.Domain.Poker;

public enum Street { Preflop, Flop, Turn, River, Showdown }
```

- [ ] **Step 4: Implement `PokerAction.cs`** (discriminated union via abstract record)

```csharp
namespace AgentBattle.Domain.Poker;

public abstract record PokerAction(int Seat)
{
    public sealed record Fold(int Seat) : PokerAction(Seat);
    public sealed record Check(int Seat) : PokerAction(Seat);
    public sealed record Call(int Seat) : PokerAction(Seat);
    public sealed record Raise(int Seat, int Amount) : PokerAction(Seat);
    public sealed record AllIn(int Seat) : PokerAction(Seat);
}
```

- [ ] **Step 5: Implement `LegalActions.cs`**

```csharp
namespace AgentBattle.Domain.Poker;

public sealed record LegalActions(
    bool CanCheck,
    bool CanCall, int CallAmount,
    bool CanRaise, int MinRaiseTotal, int MaxRaiseTotal,
    bool CanFold);
```

- [ ] **Step 6: Implement `PokerState.cs`** — scoped view returned by `get_my_state`

```csharp
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
```

- [ ] **Step 7: Run tests, confirm pass**

Run: `dotnet test tests/AgentBattle.Domain.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```pwsh
git add -A
git commit -m "feat(domain): add poker action, state, and legal-action types"
```

### Task 2.3: Domain types for agent profile, battle config, and battle events

**Files:**
- Create: `src/AgentBattle.Domain/Battles/AgentProfile.cs`
- Create: `src/AgentBattle.Domain/Battles/BattleConfig.cs`
- Create: `src/AgentBattle.Domain/Battles/BattleEvent.cs`
- Create: `src/AgentBattle.Domain/Json/BattleEventJsonOptions.cs`
- Create: `tests/AgentBattle.Domain.Tests/Battles/BattleEventJsonTests.cs`

- [ ] **Step 1: Write failing test for JSONL round-trip**

Create `tests/AgentBattle.Domain.Tests/Battles/BattleEventJsonTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run, confirm fail**

Expected: FAIL — types missing.

- [ ] **Step 3: Implement `AgentProfile.cs`**

```csharp
namespace AgentBattle.Domain.Battles;

public sealed record AgentProfile(
    string Id,
    string DisplayName,
    string BaseUrl,
    string Model,
    string ApiKeyEnv,
    double Temperature = 0.7,
    int MaxTokens = 1500,
    int TimeoutSeconds = 60,
    string PersonaPrompt = "");
```

- [ ] **Step 4: Implement `BattleConfig.cs`**

```csharp
namespace AgentBattle.Domain.Battles;

public sealed record BattleConfig(
    string Game,
    int Hands,
    int StartingStack,
    BlindsConfig Blinds,
    IReadOnlyList<SeatAssignment> Seats);

public sealed record BlindsConfig(int Small, int Big);
public sealed record SeatAssignment(int Seat, string Agent);
```

- [ ] **Step 5: Implement `BattleEvent.cs`** — polymorphic hierarchy

```csharp
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
```

- [ ] **Step 6: Implement `BattleEventJsonOptions.cs`** — pinned serializer options

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentBattle.Domain.Json;

public static class BattleEventJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}
```

Note: `Card` will serialize as an object by default. To make it a string, add an explicit converter. Add this to the same file:

```csharp
public sealed class CardJsonConverter : JsonConverter<AgentBattle.Domain.Cards.Card>
{
    public override AgentBattle.Domain.Cards.Card Read(ref Utf8JsonReader reader, System.Type t, JsonSerializerOptions o)
        => AgentBattle.Domain.Cards.Card.Parse(reader.GetString()!);
    public override void Write(Utf8JsonWriter writer, AgentBattle.Domain.Cards.Card value, JsonSerializerOptions o)
        => writer.WriteStringValue(value.ToString());
}
```

And register it: in `BattleEventJsonOptions.Default`, add `new CardJsonConverter()` to the `Converters` list.

- [ ] **Step 7: Run tests, confirm pass**

Run: `dotnet test tests/AgentBattle.Domain.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```pwsh
git add -A
git commit -m "feat(domain): add battle events, config, and JSONL serializer options"
```

### Task 2.4: Card deck with deterministic-shuffle seed

**Files:**
- Create: `src/AgentBattle.Poker.Mcp/AgentBattle.Poker.Mcp.csproj` (console)
- Create: `src/AgentBattle.Poker.Mcp/Engine/Deck.cs`
- Create: `tests/AgentBattle.Poker.Mcp.Tests/AgentBattle.Poker.Mcp.Tests.csproj`
- Create: `tests/AgentBattle.Poker.Mcp.Tests/Engine/DeckTests.cs`

- [ ] **Step 1: Scaffold projects**

```pwsh
dotnet new console -n AgentBattle.Poker.Mcp -o src/AgentBattle.Poker.Mcp
dotnet new xunit -n AgentBattle.Poker.Mcp.Tests -o tests/AgentBattle.Poker.Mcp.Tests
dotnet sln add src/AgentBattle.Poker.Mcp/AgentBattle.Poker.Mcp.csproj
dotnet sln add tests/AgentBattle.Poker.Mcp.Tests/AgentBattle.Poker.Mcp.Tests.csproj
dotnet add src/AgentBattle.Poker.Mcp reference src/AgentBattle.Domain
dotnet add tests/AgentBattle.Poker.Mcp.Tests reference src/AgentBattle.Poker.Mcp
dotnet add tests/AgentBattle.Poker.Mcp.Tests package FluentAssertions
```

- [ ] **Step 2: Write failing tests for Deck**

Create `tests/AgentBattle.Poker.Mcp.Tests/Engine/DeckTests.cs`:

```csharp
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class DeckTests
{
    [Fact]
    public void Fresh_deck_contains_52_unique_cards()
    {
        var deck = new Deck(seed: 1);
        var drawn = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 52; i++) drawn.Add(deck.Draw().ToString());
        drawn.Count.Should().Be(52);
    }

    [Fact]
    public void Drawing_53_cards_throws()
    {
        var deck = new Deck(seed: 1);
        for (int i = 0; i < 52; i++) deck.Draw();
        var act = () => deck.Draw();
        act.Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Same_seed_produces_same_order()
    {
        var d1 = new Deck(seed: 42);
        var d2 = new Deck(seed: 42);
        for (int i = 0; i < 52; i++)
            d1.Draw().Should().Be(d2.Draw());
    }
}
```

- [ ] **Step 3: Run, confirm fail**

Expected: FAIL — `Deck` does not exist.

- [ ] **Step 4: Implement `Deck.cs`**

```csharp
using AgentBattle.Domain.Cards;

namespace AgentBattle.Poker.Mcp.Engine;

public sealed class Deck
{
    private readonly Card[] _cards = new Card[52];
    private int _next;

    public Deck(int seed)
    {
        var i = 0;
        foreach (var r in System.Enum.GetValues<Rank>())
        foreach (var s in System.Enum.GetValues<Suit>())
            _cards[i++] = new Card(r, s);

        // Fisher–Yates with a seeded Random for reproducibility.
        var rng = new System.Random(seed);
        for (var k = _cards.Length - 1; k > 0; k--)
        {
            var j = rng.Next(k + 1);
            (_cards[k], _cards[j]) = (_cards[j], _cards[k]);
        }
    }

    public Card Draw()
    {
        if (_next >= _cards.Length)
            throw new System.InvalidOperationException("Deck exhausted");
        return _cards[_next++];
    }
}
```

- [ ] **Step 5: Run tests, confirm pass**

Run: `dotnet test tests/AgentBattle.Poker.Mcp.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```pwsh
git add -A
git commit -m "feat(engine): add seeded Deck for reproducible shuffles"
```

### Task 2.5: 7-card hand evaluator

A hand evaluator takes 5–7 cards and returns a comparable rank. The simplest correct approach for our scale (≤6 showdowns per hand × 50 hands): enumerate every 5-card subset of the available cards, score each subset, keep the best. Performance is irrelevant here.

**Files:**
- Create: `src/AgentBattle.Poker.Mcp/Engine/HandEvaluator.cs`
- Create: `tests/AgentBattle.Poker.Mcp.Tests/Engine/HandEvaluatorTests.cs`

- [ ] **Step 1: Write failing tests for hand ranking**

Create `tests/AgentBattle.Poker.Mcp.Tests/Engine/HandEvaluatorTests.cs`:

```csharp
using AgentBattle.Domain.Cards;
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class HandEvaluatorTests
{
    private static HandRank Eval(params string[] cards)
        => HandEvaluator.Evaluate([.. System.Linq.Enumerable.Select(cards, Card.Parse)]);

    [Fact] public void Detects_royal_flush()      => Eval("As","Ks","Qs","Js","Ts","2c","3d").Category.Should().Be(HandCategory.StraightFlush);
    [Fact] public void Detects_quads()            => Eval("Ah","Ad","Ac","As","2d","3c","4s").Category.Should().Be(HandCategory.FourOfAKind);
    [Fact] public void Detects_full_house()       => Eval("Ah","Ad","Ac","Kh","Kd","2c","3s").Category.Should().Be(HandCategory.FullHouse);
    [Fact] public void Detects_flush()            => Eval("Ah","Th","8h","6h","2h","3c","3s").Category.Should().Be(HandCategory.Flush);
    [Fact] public void Detects_straight()         => Eval("9h","8d","7c","6s","5h","Kd","Qc").Category.Should().Be(HandCategory.Straight);
    [Fact] public void Detects_wheel_A_to_5()     => Eval("Ah","2d","3c","4s","5h","Kd","Qc").Category.Should().Be(HandCategory.Straight);
    [Fact] public void Detects_trips()            => Eval("9h","9d","9c","Ks","Qh","Jc","2s").Category.Should().Be(HandCategory.ThreeOfAKind);
    [Fact] public void Detects_two_pair()         => Eval("9h","9d","8c","8s","Kh","Jc","2s").Category.Should().Be(HandCategory.TwoPair);
    [Fact] public void Detects_pair()             => Eval("9h","9d","Kc","8s","6h","Jc","2s").Category.Should().Be(HandCategory.Pair);
    [Fact] public void Detects_high_card()        => Eval("Ah","Kd","9c","8s","6h","Jc","2s").Category.Should().Be(HandCategory.HighCard);

    [Fact]
    public void Higher_pair_beats_lower_pair()
    {
        var aces = Eval("Ah","Ad","Kc","8s","6h","Jc","2s");
        var nines = Eval("9h","9d","Kc","8s","6h","Jc","2s");
        aces.CompareTo(nines).Should().BePositive();
    }

    [Fact]
    public void Kicker_breaks_ties_for_same_pair()
    {
        var withK = Eval("9h","9d","Kc","8s","6h","Jc","2s");
        var withQ = Eval("9h","9d","Qc","8s","6h","Jc","2s");
        withK.CompareTo(withQ).Should().BePositive();
    }
}
```

- [ ] **Step 2: Run, confirm fail**

Expected: FAIL — `HandEvaluator`, `HandRank`, `HandCategory` do not exist.

- [ ] **Step 3: Implement `HandEvaluator.cs`**

```csharp
using AgentBattle.Domain.Cards;

namespace AgentBattle.Poker.Mcp.Engine;

public enum HandCategory
{
    HighCard, Pair, TwoPair, ThreeOfAKind, Straight, Flush, FullHouse, FourOfAKind, StraightFlush
}

public readonly record struct HandRank(HandCategory Category, IReadOnlyList<int> Tiebreak, string Description)
    : System.IComparable<HandRank>
{
    public int CompareTo(HandRank other)
    {
        var c = Category.CompareTo(other.Category);
        if (c != 0) return c;
        for (var i = 0; i < System.Math.Min(Tiebreak.Count, other.Tiebreak.Count); i++)
        {
            var diff = Tiebreak[i].CompareTo(other.Tiebreak[i]);
            if (diff != 0) return diff;
        }
        return 0;
    }
}

public static class HandEvaluator
{
    public static HandRank Evaluate(IReadOnlyList<Card> available)
    {
        if (available.Count < 5) throw new System.ArgumentException("Need at least 5 cards");
        HandRank best = default;
        var first = true;
        foreach (var combo in Combinations(available, 5))
        {
            var rank = ScoreFive(combo);
            if (first || rank.CompareTo(best) > 0) { best = rank; first = false; }
        }
        return best;
    }

    private static HandRank ScoreFive(IReadOnlyList<Card> five)
    {
        var ranks = five.Select(c => (int)c.Rank).OrderDescending().ToArray();
        var suits = five.Select(c => c.Suit).ToArray();
        var isFlush = suits.Distinct().Count() == 1;
        var isStraight = IsStraight(ranks, out var straightHigh);
        var groups = ranks.GroupBy(r => r).Select(g => (Rank: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ThenByDescending(g => g.Rank).ToArray();

        if (isFlush && isStraight) return new(HandCategory.StraightFlush, [straightHigh], $"Straight flush, {RankName(straightHigh)} high");
        if (groups[0].Count == 4) return new(HandCategory.FourOfAKind, [groups[0].Rank, groups[1].Rank], $"Four of a kind, {RankName(groups[0].Rank)}s");
        if (groups[0].Count == 3 && groups[1].Count == 2) return new(HandCategory.FullHouse, [groups[0].Rank, groups[1].Rank], $"Full house, {RankName(groups[0].Rank)}s over {RankName(groups[1].Rank)}s");
        if (isFlush) return new(HandCategory.Flush, ranks, $"Flush, {RankName(ranks[0])} high");
        if (isStraight) return new(HandCategory.Straight, [straightHigh], $"Straight, {RankName(straightHigh)} high");
        if (groups[0].Count == 3) return new(HandCategory.ThreeOfAKind, [groups[0].Rank, groups[1].Rank, groups[2].Rank], $"Three of a kind, {RankName(groups[0].Rank)}s");
        if (groups[0].Count == 2 && groups[1].Count == 2) return new(HandCategory.TwoPair, [groups[0].Rank, groups[1].Rank, groups[2].Rank], $"Two pair, {RankName(groups[0].Rank)}s and {RankName(groups[1].Rank)}s");
        if (groups[0].Count == 2) return new(HandCategory.Pair, [groups[0].Rank, groups[1].Rank, groups[2].Rank, groups[3].Rank], $"Pair of {RankName(groups[0].Rank)}s");
        return new(HandCategory.HighCard, ranks, $"High card {RankName(ranks[0])}");
    }

    private static bool IsStraight(int[] descRanks, out int high)
    {
        // descRanks is 5 ints, sorted desc, possibly with duplicates eliminated by caller? Not here — but pairs disqualify.
        if (descRanks.Distinct().Count() != 5) { high = 0; return false; }
        // Wheel: A,5,4,3,2
        if (descRanks[0] == 14 && descRanks[1] == 5 && descRanks[2] == 4 && descRanks[3] == 3 && descRanks[4] == 2)
        { high = 5; return true; }
        for (var i = 1; i < descRanks.Length; i++)
            if (descRanks[i] != descRanks[i - 1] - 1) { high = 0; return false; }
        high = descRanks[0];
        return true;
    }

    private static string RankName(int r) => r switch
    {
        14 => "Ace", 13 => "King", 12 => "Queen", 11 => "Jack", 10 => "Ten",
        _ => r.ToString()
    };

    private static IEnumerable<IReadOnlyList<Card>> Combinations(IReadOnlyList<Card> cards, int k)
    {
        var idx = new int[k];
        for (var i = 0; i < k; i++) idx[i] = i;
        while (true)
        {
            yield return idx.Select(i => cards[i]).ToArray();
            var p = k - 1;
            while (p >= 0 && idx[p] == cards.Count - k + p) p--;
            if (p < 0) yield break;
            idx[p]++;
            for (var i = p + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
        }
    }
}
```

- [ ] **Step 4: Run tests, confirm pass**

Run: `dotnet test tests/AgentBattle.Poker.Mcp.Tests`
Expected: PASS — all 12 hand evaluator tests green.

- [ ] **Step 5: Commit**

```pwsh
git add -A
git commit -m "feat(engine): 7-card Texas Hold'em hand evaluator"
```

### Task 2.6: Pot manager with side pots

When players go all-in for different amounts, the pot splits. Smallest all-in player can only win up to their contribution from every other player; remaining bets form a side pot.

**Files:**
- Create: `src/AgentBattle.Poker.Mcp/Engine/PotManager.cs`
- Create: `tests/AgentBattle.Poker.Mcp.Tests/Engine/PotManagerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AgentBattle.Poker.Mcp.Tests/Engine/PotManagerTests.cs`:

```csharp
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class PotManagerTests
{
    [Fact]
    public void Single_pot_when_no_one_is_short()
    {
        var pots = PotManager.BuildPots(new() { [0] = 100, [1] = 100, [2] = 100 }, foldedSeats: new());
        pots.Should().HaveCount(1);
        pots[0].Amount.Should().Be(300);
        pots[0].EligibleSeats.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    [Fact]
    public void Side_pot_when_one_player_short_all_in()
    {
        // Seat 0 in for 40 (all-in), seats 1+2 in for 100.
        var pots = PotManager.BuildPots(new() { [0] = 40, [1] = 100, [2] = 100 }, foldedSeats: new());
        pots.Should().HaveCount(2);
        pots[0].Amount.Should().Be(120);  // 3 × 40
        pots[0].EligibleSeats.Should().BeEquivalentTo(new[] { 0, 1, 2 });
        pots[1].Amount.Should().Be(120);  // 2 × 60
        pots[1].EligibleSeats.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Folded_seats_contribute_but_are_not_eligible()
    {
        var pots = PotManager.BuildPots(new() { [0] = 50, [1] = 100, [2] = 100 }, foldedSeats: new() { 0 });
        pots.Sum(p => p.Amount).Should().Be(250);
        pots.SelectMany(p => p.EligibleSeats).Should().NotContain(0);
    }
}
```

- [ ] **Step 2: Run, confirm fail**

Expected: FAIL — `PotManager` does not exist.

- [ ] **Step 3: Implement `PotManager.cs`**

```csharp
namespace AgentBattle.Poker.Mcp.Engine;

public sealed record SidePot(int Amount, IReadOnlyList<int> EligibleSeats);

public static class PotManager
{
    public static IReadOnlyList<SidePot> BuildPots(Dictionary<int, int> contributions, HashSet<int> foldedSeats)
    {
        var result = new List<SidePot>();
        var working = new Dictionary<int, int>(contributions);
        while (working.Values.Any(v => v > 0))
        {
            var floor = working.Where(kv => kv.Value > 0).Min(kv => kv.Value);
            var contributors = working.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToArray();
            var amount = floor * contributors.Length;
            var eligible = contributors.Where(s => !foldedSeats.Contains(s)).ToArray();
            result.Add(new SidePot(amount, eligible));
            foreach (var s in contributors) working[s] -= floor;
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```pwsh
git add -A
git commit -m "feat(engine): pot manager with side-pot splits"
```

### Task 2.7: Betting round mechanics

A betting round tracks who has acted, the current bet level, and when the round is closed. Round closes when every active (non-folded, non-all-in) seat has either matched the current bet or had the chance to act since the last raise.

**Files:**
- Create: `src/AgentBattle.Poker.Mcp/Engine/BettingRound.cs`
- Create: `tests/AgentBattle.Poker.Mcp.Tests/Engine/BettingRoundTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AgentBattle.Poker.Mcp.Tests/Engine/BettingRoundTests.cs`:

```csharp
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class BettingRoundTests
{
    private static BettingRound NewRound(int[] activeSeats, int startSeat, int bigBlind = 20)
        => new BettingRound(activeSeats, startSeat, currentBet: bigBlind, minRaise: bigBlind);

    [Fact]
    public void Round_starts_open_with_first_seat_to_act()
    {
        var r = NewRound([0, 1, 2], startSeat: 0);
        r.IsClosed.Should().BeFalse();
        r.CurrentSeat.Should().Be(0);
    }

    [Fact]
    public void Check_advances_to_next_seat_when_no_outstanding_bet()
    {
        var r = new BettingRound([0, 1, 2], 0, currentBet: 0, minRaise: 20);
        r.RecordCheck(0);
        r.CurrentSeat.Should().Be(1);
    }

    [Fact]
    public void Round_closes_after_all_seats_call_the_initial_bet()
    {
        var r = NewRound([0, 1, 2], 0);
        r.RecordCall(0, amount: 20);
        r.RecordCall(1, amount: 20);
        r.RecordCheck(2);  // BB has option, checks
        r.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Raise_reopens_action_for_seats_that_already_acted()
    {
        var r = NewRound([0, 1, 2], 0);
        r.RecordCall(0, amount: 20);
        r.RecordRaiseTo(1, totalBet: 60);
        r.CurrentSeat.Should().Be(2);
        r.IsClosed.Should().BeFalse();
        r.RecordCall(2, amount: 60);
        r.CurrentSeat.Should().Be(0);
        r.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Folds_remove_seat_from_active_list()
    {
        var r = NewRound([0, 1, 2], 0);
        r.RecordFold(0);
        r.ActiveSeats.Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
```

- [ ] **Step 2: Run, confirm fail**

Expected: FAIL.

- [ ] **Step 3: Implement `BettingRound.cs`**

```csharp
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
        if (idx < 0) idx = -1; // current seat folded; pick up from after
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
```

Note: this is a deliberately simple model. All-in / short-stack handling is delegated to `PokerGame` — `BettingRound` just tracks "who's still acting." The game removes a seat from `activeSeats` when they go all-in (treats them like a fold for action purposes), and re-adds their contribution to the pot.

- [ ] **Step 4: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```pwsh
git add -A
git commit -m "feat(engine): betting round action tracking and closure rules"
```

### Task 2.8: PokerGame state machine (full hand integration)

**Files:**
- Create: `src/AgentBattle.Poker.Mcp/Engine/PokerGame.cs`
- Create: `tests/AgentBattle.Poker.Mcp.Tests/Engine/PokerGameTests.cs`

- [ ] **Step 1: Write the integration test for one full hand**

Create `tests/AgentBattle.Poker.Mcp.Tests/Engine/PokerGameTests.cs`:

```csharp
using AgentBattle.Domain.Poker;
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Engine;

public class PokerGameTests
{
    [Fact]
    public void Full_hand_three_handed_everyone_calls_to_showdown()
    {
        var game = new PokerGame(
            seats: [0, 1, 2],
            startingStacks: new() { [0] = 1000, [1] = 1000, [2] = 1000 },
            agentNames: new() { [0] = "A", [1] = "B", [2] = "C" },
            smallBlind: 10, bigBlind: 20,
            buttonSeat: 0, deckSeed: 1);

        game.StartHand();
        game.Street.Should().Be(Street.Preflop);
        game.CurrentSeat.Should().Be(0); // UTG = seat after BB in 3-handed; SB=1, BB=2, button=0, so action starts at button preflop in 3-handed

        // Loop: every active seat calls to the BB, then everyone checks each street.
        while (game.Street != Street.Showdown)
        {
            var seat = game.CurrentSeat;
            var state = game.GetMyState(seat);
            if (state.Legal.CanCheck) game.Apply(new PokerAction.Check(seat));
            else game.Apply(new PokerAction.Call(seat));
        }

        var result = game.ResolveShowdown();
        result.Winners.Should().NotBeEmpty();
        result.Stacks.Values.Sum().Should().Be(3000); // chips conserved
    }

    [Fact]
    public void Fold_to_one_wins_pot_without_showdown()
    {
        var game = new PokerGame([0, 1], new() { [0] = 1000, [1] = 1000 }, new() { [0] = "A", [1] = "B" }, 10, 20, buttonSeat: 0, deckSeed: 2);
        game.StartHand();
        // Heads-up: button=SB acts first preflop. Seat 0 folds.
        game.Apply(new PokerAction.Fold(0));
        var result = game.ResolveShowdown();
        result.Stacks[1].Should().Be(1010); // won the 10 small blind from seat 0 (since BB folded by way of seat 0 folding to BB)
        result.Stacks[0].Should().Be(990);
    }
}
```

Note: the SB/BB position semantics for heads-up vs 3+ handed are subtly different. Document inside `PokerGame`: in 3+ handed, button → SB → BB → UTG, action starts UTG preflop; heads-up, button is SB and acts first preflop. The test above uses the simpler invariant (chips conserved + correct winner on instant fold).

- [ ] **Step 2: Run, confirm fail**

Expected: FAIL — `PokerGame` does not exist.

- [ ] **Step 3: Implement `PokerGame.cs`**

This is the largest single file in M2. It's a state machine that wires Deck, BettingRound, PotManager, and HandEvaluator together. Implement these public methods, in order:

- `void StartHand()` — rotates button, posts blinds (deducts from stacks, adds to current-bet tracker), deals two hole cards per active seat, opens preflop betting round.
- `PokerState GetMyState(int seat)` — projects the world into the seat's scoped view. Hole cards from `_hole[seat]`, legal actions from current round + stack.
- `ApplyResult Apply(PokerAction action)` — validates action against legal actions; if invalid returns `ApplyResult.Rejected(reason)`. If valid, updates round + stack + contributions and returns `ApplyResult.Ok(applied)`.
- `void AdvanceStreetIfRoundClosed()` — called after each `Apply`. If round closed: deal flop/turn/river or move to showdown. Open the next round with action starting from the seat left of the button.
- `ShowdownResult ResolveShowdown()` — builds pots via `PotManager`, evaluates each remaining hand via `HandEvaluator`, awards each side pot to its best eligible hand, returns updated stacks.

Internal state:

```csharp
private readonly int[] _seats;
private readonly Dictionary<int, int> _stacks;
private readonly Dictionary<int, string> _names;
private readonly int _sb, _bb;
private int _button;
private int _handNo;
private Street _street;
private Deck _deck = null!;
private readonly Dictionary<int, List<Card>> _hole = new();
private readonly List<Card> _community = new();
private readonly Dictionary<int, int> _streetBets = new();      // bets in the current street
private readonly Dictionary<int, int> _handContributions = new(); // total this hand for pot building
private readonly HashSet<int> _folded = new();
private readonly HashSet<int> _allIn = new();
private BettingRound _round = null!;
private readonly List<ActionLogEntry> _log = new();
```

Skeleton (fill in straightforward parts; key methods are noted):

```csharp
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Poker.Mcp.Engine;

public sealed record ApplyResult(bool Ok, string? Reason, PokerAction? Applied)
{
    public static ApplyResult Rejected(string reason) => new(false, reason, null);
    public static ApplyResult OkApplied(PokerAction a) => new(true, null, a);
}

public sealed record ShowdownResult(IReadOnlyDictionary<int, int> Stacks, IReadOnlyList<(int Seat, int Pot, string Description)> Winners, IReadOnlyDictionary<int, IReadOnlyList<Card>> Reveals);

public sealed class PokerGame
{
    // fields per skeleton above
    public PokerGame(IReadOnlyList<int> seats, Dictionary<int,int> startingStacks, Dictionary<int,string> agentNames, int smallBlind, int bigBlind, int buttonSeat, int deckSeed)
    { /* assign fields, store seed for deck-per-hand */ }

    public int CurrentSeat => _round.CurrentSeat;
    public Street Street => _street;
    public int HandNo => _handNo;

    public IReadOnlyList<int> ActiveSeats() => _seats.Where(s => !_folded.Contains(s) && _stacks[s] > 0).ToArray();

    public void StartHand() { /* increment _handNo, rotate button, reset round/community/hole/contribs/folded/allIn, post blinds, deal hole cards, open preflop round */ }

    public PokerState GetMyState(int seat) { /* see Step 4 below */ }

    public ApplyResult Apply(PokerAction action) { /* see Step 5 below */ }

    public ShowdownResult ResolveShowdown() { /* see Step 6 below */ }

    // ... private helpers: ComputeLegal(seat), AdvanceStreet(), DealStreet(), ComputeStartSeat()
}
```

- [ ] **Step 4: Implement `ComputeLegal(seat)` and `GetMyState`**

`ComputeLegal` is the critical correctness piece. Logic:

```csharp
private LegalActions ComputeLegal(int seat)
{
    var currentBet = _round.CurrentBet;
    var myBet = _streetBets.GetValueOrDefault(seat, 0);
    var toCall = System.Math.Max(0, currentBet - myBet);
    var stack = _stacks[seat];

    var canCheck = toCall == 0;
    var canCall = toCall > 0 && stack > 0;
    var callAmount = System.Math.Min(toCall, stack); // clamped if short
    var minRaiseTotal = currentBet + _round.MinRaise;
    var maxRaiseTotal = myBet + stack;
    var canRaise = stack > toCall && maxRaiseTotal >= minRaiseTotal;
    return new LegalActions(canCheck, canCall, callAmount, canRaise, minRaiseTotal, maxRaiseTotal, CanFold: !canCheck);
}
```

(Optional: allow fold when CanCheck is true too — most engines forbid this because folding when you could check for free is irrational, but some do allow it. Keep it disallowed for the MVP; cleaner agent behavior.)

`GetMyState` packages this up:

```csharp
public PokerState GetMyState(int seat)
{
    var seatSummaries = _seats.Select(s => new SeatSummary(
        Seat: s,
        AgentDisplayName: _names[s],
        Stack: _stacks[s],
        CurrentBet: _streetBets.GetValueOrDefault(s, 0),
        HasFolded: _folded.Contains(s),
        IsAllIn: _allIn.Contains(s),
        IsInactive: _stacks[s] == 0 && !_folded.Contains(s) && !_allIn.Contains(s))
    ).ToArray();

    return new PokerState(
        HandNo: _handNo,
        Street: _street,
        Seat: seat,
        HoleCards: _hole.TryGetValue(seat, out var h) ? h : [],
        Community: _community,
        MyStack: _stacks[seat],
        MyCurrentBet: _streetBets.GetValueOrDefault(seat, 0),
        Pot: _handContributions.Values.Sum(),
        ToCall: System.Math.Max(0, _round.CurrentBet - _streetBets.GetValueOrDefault(seat, 0)),
        Seats: seatSummaries,
        ActionLog: _log,
        CurrentSeat: CurrentSeat,
        Legal: ComputeLegal(seat));
}
```

- [ ] **Step 5: Implement `Apply`**

```csharp
public ApplyResult Apply(PokerAction action)
{
    if (action.Seat != CurrentSeat) return ApplyResult.Rejected("not_your_turn");
    var legal = ComputeLegal(action.Seat);
    switch (action)
    {
        case PokerAction.Fold f:
            if (!legal.CanFold) return ApplyResult.Rejected("cannot_fold_when_check_available");
            _folded.Add(f.Seat); _round.RecordFold(f.Seat);
            _log.Add(new ActionLogEntry(f.Seat, "fold", null, _street));
            break;
        case PokerAction.Check c:
            if (!legal.CanCheck) return ApplyResult.Rejected("cannot_check_facing_bet");
            _round.RecordCheck(c.Seat);
            _log.Add(new ActionLogEntry(c.Seat, "check", null, _street));
            break;
        case PokerAction.Call ca:
            if (!legal.CanCall) return ApplyResult.Rejected("nothing_to_call");
            ContributeChips(ca.Seat, legal.CallAmount);
            if (_stacks[ca.Seat] == 0) _allIn.Add(ca.Seat);
            _round.RecordCall(ca.Seat, legal.CallAmount);
            _log.Add(new ActionLogEntry(ca.Seat, "call", legal.CallAmount, _street));
            break;
        case PokerAction.Raise r:
            if (!legal.CanRaise) return ApplyResult.Rejected("cannot_raise");
            if (r.Amount < legal.MinRaiseTotal && r.Amount != legal.MaxRaiseTotal)
                return ApplyResult.Rejected("below_min_raise");
            if (r.Amount > legal.MaxRaiseTotal) return ApplyResult.Rejected("above_stack");
            var increment = r.Amount - _streetBets.GetValueOrDefault(r.Seat, 0);
            ContributeChips(r.Seat, increment);
            if (_stacks[r.Seat] == 0) _allIn.Add(r.Seat);
            _round.RecordRaiseTo(r.Seat, r.Amount);
            _log.Add(new ActionLogEntry(r.Seat, "raise", r.Amount, _street));
            break;
        case PokerAction.AllIn ai:
            var total = _streetBets.GetValueOrDefault(ai.Seat, 0) + _stacks[ai.Seat];
            return Apply(new PokerAction.Raise(ai.Seat, total));
    }
    AdvanceStreetIfRoundClosed();
    return ApplyResult.OkApplied(action);
}

private void ContributeChips(int seat, int amount)
{
    _stacks[seat] -= amount;
    _streetBets[seat] = _streetBets.GetValueOrDefault(seat, 0) + amount;
    _handContributions[seat] = _handContributions.GetValueOrDefault(seat, 0) + amount;
}
```

- [ ] **Step 6: Implement `AdvanceStreetIfRoundClosed` and `ResolveShowdown`**

```csharp
private void AdvanceStreetIfRoundClosed()
{
    if (!_round.IsClosed) return;
    var remaining = ActiveSeats().Except(_folded).ToArray();
    if (remaining.Length <= 1) { _street = Street.Showdown; return; }
    _streetBets.Clear();
    switch (_street)
    {
        case Street.Preflop: _street = Street.Flop; for (int i = 0; i < 3; i++) _community.Add(_deck.Draw()); break;
        case Street.Flop:    _street = Street.Turn; _community.Add(_deck.Draw()); break;
        case Street.Turn:    _street = Street.River; _community.Add(_deck.Draw()); break;
        case Street.River:   _street = Street.Showdown; return;
    }
    OpenNewRound();
}

private void OpenNewRound()
{
    var active = ActiveSeats().Except(_allIn).Except(_folded).ToArray();
    var start = NextSeatAfter(_button, active);
    _round = new BettingRound(active, start, currentBet: 0, minRaise: _bb);
}

public ShowdownResult ResolveShowdown()
{
    var pots = PotManager.BuildPots(new Dictionary<int, int>(_handContributions), _folded.ToHashSet());
    var winners = new List<(int, int, string)>();
    foreach (var pot in pots)
    {
        // For pots where everyone except one is folded, the lone eligible seat wins without revealing.
        if (pot.EligibleSeats.Count == 1)
        {
            _stacks[pot.EligibleSeats[0]] += pot.Amount;
            winners.Add((pot.EligibleSeats[0], pot.Amount, "uncontested"));
            continue;
        }
        var scored = pot.EligibleSeats
            .Select(s => (Seat: s, Rank: HandEvaluator.Evaluate([.._hole[s], .._community])))
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
        if (remainder > 0) _stacks[topGroup[0].Seat] += remainder; // odd chip to first
    }
    var reveals = (IReadOnlyDictionary<int, IReadOnlyList<Card>>)_hole.Where(kv => !_folded.Contains(kv.Key))
        .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Card>)kv.Value);
    return new ShowdownResult(new Dictionary<int, int>(_stacks), winners, reveals);
}

private int NextSeatAfter(int reference, IReadOnlyList<int> active) { /* find first seat in `active` clockwise after reference; in 6-max use _seats order */ ... }
```

- [ ] **Step 7: Run tests, confirm pass**

Run: `dotnet test tests/AgentBattle.Poker.Mcp.Tests`
Expected: PASS — both `PokerGameTests` plus all earlier tests.

- [ ] **Step 8: Commit**

```pwsh
git add -A
git commit -m "feat(engine): PokerGame state machine integrating betting + pots + showdown"
```

### Task 2.9: MCP server bootstrap with tool registration

**Files:**
- Modify: `src/AgentBattle.Poker.Mcp/AgentBattle.Poker.Mcp.csproj` — add `ModelContextProtocol` package reference
- Modify: `src/AgentBattle.Poker.Mcp/Program.cs`
- Create: `src/AgentBattle.Poker.Mcp/Mcp/PokerTools.cs`

This task assumes the official `ModelContextProtocol` C# SDK is the chosen package. At implementation time, verify by running `dotnet search ModelContextProtocol`; if the API differs, adapt the bootstrap call but keep the tool surface identical.

- [ ] **Step 1: Add MCP package**

```pwsh
dotnet add src/AgentBattle.Poker.Mcp package ModelContextProtocol
```

- [ ] **Step 2: Implement `PokerTools.cs`** — single static game instance per process

The MCP server holds one `PokerGame` instance. The orchestrator spawns a fresh server per battle, so process lifetime == battle lifetime. Tools:

```csharp
using AgentBattle.Domain.Poker;
using AgentBattle.Poker.Mcp.Engine;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AgentBattle.Poker.Mcp.Mcp;

[McpServerToolType]
public sealed class PokerTools
{
    private static PokerGame? _game;

    [McpServerTool, Description("Configure a new poker game for this server instance.")]
    public static string ConfigureGame(int[] seats, int[] startingStacks, string[] agentNames, int smallBlind, int bigBlind, int buttonSeat, int deckSeed)
    {
        var stacks = seats.Zip(startingStacks).ToDictionary(p => p.First, p => p.Second);
        var names = seats.Zip(agentNames).ToDictionary(p => p.First, p => p.Second);
        _game = new PokerGame(seats, stacks, names, smallBlind, bigBlind, buttonSeat, deckSeed);
        return """{"ok":true}""";
    }

    [McpServerTool, Description("Start the next hand. Posts blinds and deals hole cards.")]
    public static string StartHand()  { Require().StartHand(); return Serialize(new { ok = true, hand_no = Require().HandNo }); }

    [McpServerTool, Description("Get game state visible to a specific seat.")]
    public static string GetMyState(int seat) => Serialize(Require().GetMyState(seat));

    [McpServerTool, Description("Fold for the given seat (must be current seat).")]
    public static string Fold(int seat)  => ApplyAndSerialize(new PokerAction.Fold(seat));

    [McpServerTool, Description("Check for the given seat. Only legal when no bet is outstanding.")]
    public static string Check(int seat) => ApplyAndSerialize(new PokerAction.Check(seat));

    [McpServerTool, Description("Call the current bet for the given seat.")]
    public static string Call(int seat)  => ApplyAndSerialize(new PokerAction.Call(seat));

    [McpServerTool, Description("Raise to the given total bet level (not increment).")]
    public static string Raise(int seat, int amount) => ApplyAndSerialize(new PokerAction.Raise(seat, amount));

    [McpServerTool, Description("Push all remaining chips into the pot for the given seat.")]
    public static string AllIn(int seat) => ApplyAndSerialize(new PokerAction.AllIn(seat));

    [McpServerTool, Description("Resolve showdown when current street is Showdown. Returns winners and updated stacks.")]
    public static string ResolveShowdown() => Serialize(Require().ResolveShowdown());

    private static PokerGame Require() => _game ?? throw new System.InvalidOperationException("Call ConfigureGame first");

    private static string ApplyAndSerialize(PokerAction a)
    {
        var r = Require().Apply(a);
        return r.Ok
            ? Serialize(new { ok = true, applied = r.Applied, current_seat = Require().CurrentSeat, street = Require().Street.ToString().ToLowerInvariant() })
            : Serialize(new { ok = false, error = r.Reason, legal = Require().GetMyState(a.Seat).Legal });
    }

    private static string Serialize(object obj)
        => System.Text.Json.JsonSerializer.Serialize(obj, AgentBattle.Domain.Json.BattleEventJsonOptions.Default);
}
```

- [ ] **Step 3: Implement `Program.cs`**

```csharp
using ModelContextProtocol.Server;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
```

- [ ] **Step 4: Smoke test by hand**

Run the MCP server in stdio mode and pipe in a JSON-RPC `tools/list` request:

```pwsh
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | dotnet run --project src/AgentBattle.Poker.Mcp
```

Expected: response listing all eight tools (configure_game, start_hand, get_my_state, fold, check, call, raise, all_in, resolve_showdown).

If the JSON-RPC framing requires Content-Length headers (the stdio transport does — it's LSP-style framing), use `mcp-cli` or a small Python script instead. Document the exact smoke-test command that worked in `README.md` once verified.

- [ ] **Step 5: Commit**

```pwsh
git add -A
git commit -m "feat(mcp): expose PokerGame via MCP stdio server with tool surface"
```

### Task 2.10: Hide hole cards in `GetMyState` projection

Right now `GetMyState(seat)` returns the requested seat's own hole cards, which is correct — but the orchestrator must never accidentally receive *other* seats' cards back through this call. Pin that invariant with a test.

**Files:**
- Create: `tests/AgentBattle.Poker.Mcp.Tests/Mcp/StateProjectionTests.cs`

- [ ] **Step 1: Write the invariant test**

```csharp
using AgentBattle.Poker.Mcp.Engine;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Poker.Mcp.Tests.Mcp;

public class StateProjectionTests
{
    [Fact]
    public void GetMyState_only_reveals_requesting_seats_hole_cards()
    {
        var game = new PokerGame([0, 1, 2], new() { [0] = 1000, [1] = 1000, [2] = 1000 }, new() { [0] = "A", [1] = "B", [2] = "C" }, 10, 20, 0, deckSeed: 7);
        game.StartHand();
        var stateForSeat1 = game.GetMyState(1);
        stateForSeat1.HoleCards.Should().HaveCount(2);
        stateForSeat1.Seats.Should().NotContain(s => s.GetType().GetProperty("HoleCards") != null);
    }
}
```

- [ ] **Step 2: Run, confirm pass** — the existing `PokerState` shape never carries other seats' hole cards, so this should already pass and serves as a regression fence. If it fails, fix the projection.

- [ ] **Step 3: Commit**

```pwsh
git add -A
git commit -m "test(mcp): pin per-seat hole-card scoping invariant"
```

---

# Milestone 3 — Orchestrator & Battle runner

### Task 3.1: Battle event sink (JSONL writer)

**Files:**
- Create: `src/AgentBattle.Orchestrator/AgentBattle.Orchestrator.csproj`
- Create: `src/AgentBattle.Orchestrator/Recording/IBattleEventSink.cs`
- Create: `src/AgentBattle.Orchestrator/Recording/JsonlEventSink.cs`
- Create: `tests/AgentBattle.Orchestrator.Tests/AgentBattle.Orchestrator.Tests.csproj`
- Create: `tests/AgentBattle.Orchestrator.Tests/Recording/JsonlEventSinkTests.cs`

- [ ] **Step 1: Scaffold projects**

```pwsh
dotnet new classlib -n AgentBattle.Orchestrator -o src/AgentBattle.Orchestrator
dotnet new xunit -n AgentBattle.Orchestrator.Tests -o tests/AgentBattle.Orchestrator.Tests
dotnet sln add src/AgentBattle.Orchestrator/AgentBattle.Orchestrator.csproj
dotnet sln add tests/AgentBattle.Orchestrator.Tests/AgentBattle.Orchestrator.Tests.csproj
dotnet add src/AgentBattle.Orchestrator reference src/AgentBattle.Domain
dotnet add tests/AgentBattle.Orchestrator.Tests reference src/AgentBattle.Orchestrator
dotnet add tests/AgentBattle.Orchestrator.Tests package FluentAssertions
```

- [ ] **Step 2: Write failing test**

Create `tests/AgentBattle.Orchestrator.Tests/Recording/JsonlEventSinkTests.cs`:

```csharp
using AgentBattle.Domain.Battles;
using AgentBattle.Orchestrator.Recording;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Recording;

public class JsonlEventSinkTests
{
    [Fact]
    public async Task Writes_one_line_per_event_with_type_discriminator()
    {
        var path = System.IO.Path.GetTempFileName();
        await using (var sink = new JsonlEventSink(path))
        {
            await sink.WriteAsync(new BattleEvent.HandStarted(System.DateTimeOffset.UtcNow, 1, 0, 1, 2, []));
            await sink.WriteAsync(new BattleEvent.HandEnded(System.DateTimeOffset.UtcNow, 1, new Dictionary<int, int> { [0] = 1000, [1] = 1000 }));
        }
        var lines = await System.IO.File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("\"t\":\"hand_started\"");
        lines[1].Should().Contain("\"t\":\"hand_ended\"");
    }
}
```

- [ ] **Step 3: Run, confirm fail**

- [ ] **Step 4: Implement `IBattleEventSink.cs`**

```csharp
using AgentBattle.Domain.Battles;

namespace AgentBattle.Orchestrator.Recording;

public interface IBattleEventSink : System.IAsyncDisposable
{
    System.Threading.Tasks.Task WriteAsync(BattleEvent e, System.Threading.CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement `JsonlEventSink.cs`**

```csharp
using System.Text.Json;
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Json;

namespace AgentBattle.Orchestrator.Recording;

public sealed class JsonlEventSink : IBattleEventSink
{
    private readonly System.IO.StreamWriter _writer;
    public JsonlEventSink(string path)
    {
        var stream = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
        _writer = new System.IO.StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
    }
    public async System.Threading.Tasks.Task WriteAsync(BattleEvent e, System.Threading.CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize<BattleEvent>(e, BattleEventJsonOptions.Default);
        await _writer.WriteLineAsync(json.AsMemory(), ct);
    }
    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }
}
```

- [ ] **Step 6: Run tests, confirm pass**

- [ ] **Step 7: Commit**

```pwsh
git add -A
git commit -m "feat(orchestrator): JSONL battle event sink"
```

### Task 3.2: Agent client abstraction + OpenAI-compatible implementation

**Files:**
- Create: `src/AgentBattle.Orchestrator/Agents/IAgentClient.cs`
- Create: `src/AgentBattle.Orchestrator/Agents/AgentSession.cs`
- Create: `src/AgentBattle.Orchestrator/Agents/OpenAiCompatibleAgent.cs`
- Create: `tests/AgentBattle.Orchestrator.Tests/Agents/OpenAiCompatibleAgentTests.cs`

The agent client makes an OpenAI-compatible `POST /chat/completions` call with `tools` defined. The response carries either `tool_calls` or just `content`. Both are captured; the orchestrator decides what's a thought vs an action.

- [ ] **Step 1: Define the wire types**

In `IAgentClient.cs`:

```csharp
namespace AgentBattle.Orchestrator.Agents;

public sealed record AgentMessage(string Role, string Content);
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);
public sealed record ToolCall(string Name, string ArgumentsJson);
public sealed record AgentReply(string? Content, IReadOnlyList<ToolCall> ToolCalls, int Tokens);

public interface IAgentClient
{
    System.Threading.Tasks.Task<AgentReply> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        System.Threading.CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement `AgentSession.cs`** — message history container

```csharp
namespace AgentBattle.Orchestrator.Agents;

public sealed class AgentSession(string agentId, string displayName, IAgentClient client, IReadOnlyList<ToolDefinition> tools, string systemPrompt)
{
    public string AgentId { get; } = agentId;
    public string DisplayName { get; } = displayName;
    private readonly List<AgentMessage> _history = [new AgentMessage("system", systemPrompt)];

    public System.Threading.Tasks.Task<AgentReply> SendUserAsync(string content, System.Threading.CancellationToken ct = default)
    {
        _history.Add(new AgentMessage("user", content));
        return client.ChatAsync(_history, tools, ct);
    }

    public void RecordAssistantReply(AgentReply reply)
    {
        var content = reply.Content ?? "";
        if (reply.ToolCalls.Count > 0)
            content += " [called " + string.Join(", ", reply.ToolCalls.Select(t => $"{t.Name}({t.ArgumentsJson})")) + "]";
        _history.Add(new AgentMessage("assistant", content));
    }
}
```

- [ ] **Step 3: Write failing tests for `OpenAiCompatibleAgent` using a stub HTTP handler**

```csharp
using System.Net;
using System.Net.Http;
using AgentBattle.Orchestrator.Agents;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Agents;

public class OpenAiCompatibleAgentTests
{
    [Fact]
    public async Task Parses_response_with_content_and_tool_call()
    {
        var responseJson = """
        {
          "choices": [{
            "message": {
              "role": "assistant",
              "content": "I think I should raise here.",
              "tool_calls": [{
                "id": "call_1",
                "type": "function",
                "function": { "name": "raise", "arguments": "{\"amount\":60}" }
              }]
            }
          }],
          "usage": { "total_tokens": 142 }
        }
        """;
        var handler = new StubHandler(responseJson);
        var client = new OpenAiCompatibleAgent(new HttpClient(handler), baseUrl: "http://stub/v1", model: "m", apiKey: "k", temperature: 0.7, maxTokens: 1500);

        var reply = await client.ChatAsync(
            [new AgentMessage("system", "you are a player"), new AgentMessage("user", "your turn")],
            [new ToolDefinition("raise", "raise to amount", "{}")]);

        reply.Content.Should().Contain("raise here");
        reply.ToolCalls.Should().HaveCount(1);
        reply.ToolCalls[0].Name.Should().Be("raise");
        reply.ToolCalls[0].ArgumentsJson.Should().Be("{\"amount\":60}");
        reply.Tokens.Should().Be(142);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
```

- [ ] **Step 4: Run, confirm fail**

- [ ] **Step 5: Implement `OpenAiCompatibleAgent.cs`**

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentBattle.Orchestrator.Agents;

public sealed class OpenAiCompatibleAgent(HttpClient http, string baseUrl, string model, string apiKey, double temperature, int maxTokens) : IAgentClient
{
    public async System.Threading.Tasks.Task<AgentReply> ChatAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<ToolDefinition> tools, System.Threading.CancellationToken ct = default)
    {
        var req = new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            tools = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonNode.Parse(t.ParametersJsonSchema)
                }
            }).ToArray(),
            temperature,
            max_tokens = maxTokens
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = JsonContent.Create(req)
        };
        if (!string.IsNullOrEmpty(apiKey))
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await http.SendAsync(message, ct);
        resp.EnsureSuccessStatusCode();
        var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;

        var msg = node["choices"]![0]!["message"]!;
        var content = msg["content"]?.GetValue<string>();
        var toolCallsNode = msg["tool_calls"]?.AsArray();
        var toolCalls = toolCallsNode == null
            ? (IReadOnlyList<ToolCall>)[]
            : toolCallsNode.Select(tc => new ToolCall(
                tc!["function"]!["name"]!.GetValue<string>(),
                tc["function"]!["arguments"]!.GetValue<string>())).ToArray();
        var tokens = node["usage"]?["total_tokens"]?.GetValue<int>() ?? 0;
        return new AgentReply(content, toolCalls, tokens);
    }
}
```

- [ ] **Step 6: Run tests, confirm pass**

- [ ] **Step 7: Commit**

```pwsh
git add -A
git commit -m "feat(orchestrator): agent session + OpenAI-compatible HTTP client"
```

### Task 3.3: Prompt builder + action parser

**Files:**
- Create: `src/AgentBattle.Orchestrator/Agents/PromptBuilder.cs`
- Create: `src/AgentBattle.Orchestrator/Agents/ActionParser.cs`
- Create: `tests/AgentBattle.Orchestrator.Tests/Agents/PromptBuilderTests.cs`
- Create: `tests/AgentBattle.Orchestrator.Tests/Agents/ActionParserTests.cs`

- [ ] **Step 1: Tests for `PromptBuilder`** — deterministic, snapshot-style assertion on key fields

```csharp
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Agents;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_includes_seat_and_persona()
    {
        var profile = new AgentProfile("gpt-5", "GPT-5", "https://x", "gpt-5", "K", 0.7, 1500, 60, "Play tight.");
        var s = PromptBuilder.System(profile, seat: 3);
        s.Should().Contain("seat 3").And.Contain("Play tight.");
    }

    [Fact]
    public void Turn_message_includes_legal_actions_and_hole_cards()
    {
        var state = new PokerState(
            HandNo: 1, Street: Street.Preflop, Seat: 2,
            HoleCards: [Card.Parse("As"), Card.Parse("Kd")],
            Community: [], MyStack: 980, MyCurrentBet: 0, Pot: 30, ToCall: 20,
            Seats: [], ActionLog: [], CurrentSeat: 2,
            Legal: new LegalActions(false, true, 20, true, 40, 980, true));
        var msg = PromptBuilder.Turn(state, retryError: null);
        msg.Should().Contain("As").And.Contain("Kd").And.Contain("To call: 20")
           .And.Contain("call").And.Contain("raise").And.Contain("fold")
           .And.NotContain("check");
    }
}
```

- [ ] **Step 2: Implement `PromptBuilder.cs`**

```csharp
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

public static class PromptBuilder
{
    public static string System(AgentProfile profile, int seat) => $"""
        You are {profile.DisplayName}, playing 6-max No-Limit Texas Hold'em.
        Starting stacks: 1000. Blinds: 10/20. 50 hands total. You are seat {seat}.
        On each turn you will receive your scoped game state. Respond with your
        reasoning in natural prose, then call exactly one action tool: fold, check,
        call, raise, or all_in.

        {profile.PersonaPrompt}
        """;

    public static string Turn(PokerState state, string? retryError)
    {
        var sb = new System.Text.StringBuilder();
        if (retryError != null)
            sb.AppendLine($"Your previous action was rejected: {retryError}. Try again.").AppendLine();
        sb.AppendLine($"Hand {state.HandNo}, {state.Street.ToString().ToLowerInvariant()}. Your turn.");
        sb.AppendLine();
        sb.AppendLine($"Your hole cards: {string.Join(" ", state.HoleCards)}");
        sb.AppendLine($"Community: {(state.Community.Count == 0 ? "(none yet)" : string.Join(" ", state.Community))}");
        sb.AppendLine($"Your stack: {state.MyStack}");
        sb.AppendLine($"Pot: {state.Pot}");
        sb.AppendLine($"To call: {state.ToCall}");
        if (state.Legal.CanRaise) sb.AppendLine($"Min raise to: {state.Legal.MinRaiseTotal}. Max (all-in): {state.Legal.MaxRaiseTotal}.");
        sb.AppendLine($"Action so far: {(state.ActionLog.Count == 0 ? "(no actions yet)" : string.Join("; ", state.ActionLog.Select(e => $"seat {e.Seat} {e.Action}{(e.Amount != null ? " " + e.Amount : "")}")))}");
        sb.AppendLine();
        var legal = new List<string>();
        if (state.Legal.CanCheck) legal.Add("check");
        if (state.Legal.CanCall) legal.Add($"call ({state.Legal.CallAmount})");
        if (state.Legal.CanRaise) legal.Add($"raise (to a total between {state.Legal.MinRaiseTotal} and {state.Legal.MaxRaiseTotal})");
        if (state.Legal.CanFold) legal.Add("fold");
        sb.AppendLine($"Legal actions: {string.Join(", ", legal)}.");
        sb.AppendLine();
        sb.AppendLine("Reply with your reasoning then call exactly one action tool.");
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Tests for `ActionParser`**

```csharp
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Orchestrator.Tests.Agents;

public class ActionParserTests
{
    [Theory]
    [InlineData("fold",   "{}",                  typeof(PokerAction.Fold))]
    [InlineData("check",  "{}",                  typeof(PokerAction.Check))]
    [InlineData("call",   "{}",                  typeof(PokerAction.Call))]
    [InlineData("all_in", "{}",                  typeof(PokerAction.AllIn))]
    public void Parses_action_for_seat(string name, string args, System.Type expectedType)
    {
        var parsed = ActionParser.Parse(new ToolCall(name, args), seat: 4);
        parsed.IsT0.Should().BeTrue();
        parsed.AsT0.Should().BeOfType(expectedType);
    }

    [Fact]
    public void Parses_raise_with_amount()
    {
        var parsed = ActionParser.Parse(new ToolCall("raise", "{\"amount\":60}"), seat: 4);
        parsed.AsT0.Should().BeOfType<PokerAction.Raise>().Which.Amount.Should().Be(60);
    }

    [Fact]
    public void Returns_error_for_unknown_tool()
    {
        var parsed = ActionParser.Parse(new ToolCall("nuke", "{}"), seat: 4);
        parsed.IsT1.Should().BeTrue();
        parsed.AsT1.Should().Contain("unknown_tool");
    }

    [Fact]
    public void Returns_error_for_raise_without_amount()
    {
        var parsed = ActionParser.Parse(new ToolCall("raise", "{}"), seat: 4);
        parsed.IsT1.Should().BeTrue();
        parsed.AsT1.Should().Contain("missing_amount");
    }
}
```

For the `OneOf` style return: add NuGet `OneOf` to the orchestrator project (`dotnet add src/AgentBattle.Orchestrator package OneOf`), and to the test project. Alternatively, return a simple `(PokerAction? Action, string? Error)` tuple — that's fine too. The implementation below uses the tuple variant for fewer dependencies; update the tests to match if you go that route.

- [ ] **Step 4: Implement `ActionParser.cs` (tuple variant)**

Adjust the tests to read `var (action, error) = ActionParser.Parse(...);`.

```csharp
using System.Text.Json;
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Agents;

public static class ActionParser
{
    public static (PokerAction? Action, string? Error) Parse(ToolCall call, int seat)
    {
        return call.Name switch
        {
            "fold"   => (new PokerAction.Fold(seat), null),
            "check"  => (new PokerAction.Check(seat), null),
            "call"   => (new PokerAction.Call(seat), null),
            "all_in" => (new PokerAction.AllIn(seat), null),
            "raise"  => ParseRaise(seat, call.ArgumentsJson),
            _ => (null, $"unknown_tool: {call.Name}")
        };
    }

    private static (PokerAction?, string?) ParseRaise(int seat, string args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (!doc.RootElement.TryGetProperty("amount", out var amt))
                return (null, "missing_amount");
            return (new PokerAction.Raise(seat, amt.GetInt32()), null);
        }
        catch (JsonException ex)
        {
            return (null, $"invalid_arguments_json: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Run tests, confirm pass**

- [ ] **Step 6: Commit**

```pwsh
git add -A
git commit -m "feat(orchestrator): prompt builder and tool-call action parser"
```

### Task 3.4: MCP game client (talks to the spawned poker MCP server)

**Files:**
- Create: `src/AgentBattle.Orchestrator/Mcp/McpGameClient.cs`

The orchestrator launches the poker MCP server as a child process and speaks JSON-RPC stdio to it. The official C# MCP SDK provides a client-side abstraction. At implementation time, verify the SDK package name and replace this wrapper if a higher-level client exists. The wrapper below exposes only the surface the orchestrator needs.

- [ ] **Step 1: Add MCP client package** — same package as the server: `dotnet add src/AgentBattle.Orchestrator package ModelContextProtocol`

- [ ] **Step 2: Implement `McpGameClient.cs`** with this surface:

```csharp
using AgentBattle.Domain.Poker;

namespace AgentBattle.Orchestrator.Mcp;

public sealed class McpGameClient : System.IAsyncDisposable
{
    // Holds an MCP client instance configured with stdio transport pointed at the spawned process.
    public static System.Threading.Tasks.Task<McpGameClient> SpawnAsync(string serverExecutablePath, System.Threading.CancellationToken ct = default)
        => throw new System.NotImplementedException("Wire up using ModelContextProtocol.Client API");

    public System.Threading.Tasks.Task ConfigureGameAsync(int[] seats, int[] startingStacks, string[] agentNames, int smallBlind, int bigBlind, int buttonSeat, int deckSeed, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();

    public System.Threading.Tasks.Task<int> StartHandAsync(System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
    public System.Threading.Tasks.Task<PokerState> GetMyStateAsync(int seat, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
    public System.Threading.Tasks.Task<(bool Ok, string? Error)> ApplyAsync(PokerAction action, System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();
    public System.Threading.Tasks.Task<ShowdownResult> ResolveShowdownAsync(System.Threading.CancellationToken ct = default) => throw new System.NotImplementedException();

    public System.Threading.Tasks.ValueTask DisposeAsync() => default;
}

// Lift ShowdownResult into the orchestrator namespace so we don't reference Poker.Mcp from Orchestrator.
public sealed record ShowdownResult(IReadOnlyDictionary<int, int> Stacks, IReadOnlyList<(int Seat, int Pot, string Description)> Winners, IReadOnlyDictionary<int, IReadOnlyList<AgentBattle.Domain.Cards.Card>> Reveals);
```

Implementation guidance for the engineer:
- The `ModelContextProtocol.Client` API exposes a client builder that takes a stdio transport pointing at an executable. Use `System.Diagnostics.Process` or the SDK's spawn helper.
- Each tool call goes through the MCP client; serialize arguments to JSON and deserialize the textual content of the response (the server-side handlers return JSON strings via `Serialize(...)`).
- Wrap each call in a try/catch and translate transport errors into `(false, "transport_error: ...")`.

- [ ] **Step 3: Smoke test** — write a small integration test that spawns the real built `AgentBattle.Poker.Mcp` and plays one heads-up hand of all-checks. Skip via xunit `[Fact(Skip="integration")]` by default; run manually with `dotnet test --filter "Category=Integration"`.

- [ ] **Step 4: Commit (the smoke test alone if necessary; the full implementation lands in the next step):**

```pwsh
git add -A
git commit -m "feat(orchestrator): MCP game client wrapping the poker server"
```

### Task 3.5: Turn loop with retry logic

**Files:**
- Create: `src/AgentBattle.Orchestrator/TurnLoop/TurnRunner.cs`
- Create: `tests/AgentBattle.Orchestrator.Tests/TurnLoop/TurnRunnerTests.cs`

The turn runner is the heart of the orchestrator: given a current seat, it gets the state from MCP, calls the agent, parses the reply, applies the action, handles retries, and emits the right events to the sink.

- [ ] **Step 1: Write failing tests with stubs**

Build stubs for `IAgentClient` (returns a scripted sequence of replies) and the MCP boundary (an in-memory fake that wraps a real `PokerGame`). Test scenarios:

1. Happy path — agent replies with prose + valid `call` tool call → one `agent_turn_started`, one `agent_thoughts`, one `agent_action`, no rejections.
2. One rejection then success — agent first replies with `raise` below min; second attempt is `call` → two prose events, one `agent_action_rejected`, one `agent_action`, attempt numbers correct.
3. Three rejections then forced default — agent replies with bad raise three times → three rejections, one final `agent_action` with `auto_reason: "retries_exhausted"` and `action: "check"` (or "fold" if checking isn't legal).
4. Empty reply (no tool call) → counts as a rejection, prompts again.

```csharp
// Sketch only; flesh out in implementation.
[Fact] public async Task Happy_path_emits_thoughts_then_action() { /* ... */ }
[Fact] public async Task Rejected_action_is_logged_and_retried() { /* ... */ }
[Fact] public async Task Three_rejections_force_check_or_fold() { /* ... */ }
[Fact] public async Task Reply_with_no_tool_call_counts_as_rejection() { /* ... */ }
```

- [ ] **Step 2: Implement `TurnRunner.cs`**

```csharp
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Poker;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Recording;

namespace AgentBattle.Orchestrator.TurnLoop;

public sealed class TurnRunner(IBattleEventSink sink, System.TimeProvider time)
{
    public async System.Threading.Tasks.Task RunOneTurnAsync(
        int seat,
        AgentSession session,
        System.Func<int, System.Threading.CancellationToken, System.Threading.Tasks.Task<PokerState>> getState,
        System.Func<PokerAction, System.Threading.CancellationToken, System.Threading.Tasks.Task<(bool Ok, string? Error)>> apply,
        IReadOnlyList<ToolDefinition> tools,
        System.Threading.CancellationToken ct = default)
    {
        var state = await getState(seat, ct);
        await sink.WriteAsync(new BattleEvent.AgentTurnStarted(time.GetUtcNow(), state.HandNo, seat, state), ct);

        string? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var msg = PromptBuilder.Turn(state, lastError);
            var reply = await session.SendUserAsync(msg, ct);
            session.RecordAssistantReply(reply);

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

            var (ok, applyErr) = await apply(action, ct);
            if (ok)
            {
                await sink.WriteAsync(new BattleEvent.AgentAction(time.GetUtcNow(), state.HandNo, seat, ActionName(action), AmountOf(action), attempt, null), ct);
                return;
            }
            lastError = applyErr;
            await sink.WriteAsync(new BattleEvent.AgentActionRejected(time.GetUtcNow(), state.HandNo, seat, ActionName(action), AmountOf(action), applyErr!, attempt), ct);
        }

        // Forced default.
        var forced = state.Legal.CanCheck ? (PokerAction)new PokerAction.Check(seat) : new PokerAction.Fold(seat);
        await apply(forced, ct);
        await sink.WriteAsync(new BattleEvent.AgentAction(time.GetUtcNow(), state.HandNo, seat, ActionName(forced), null, 4, "retries_exhausted"), ct);
    }

    private static string ActionName(PokerAction a) => a switch
    {
        PokerAction.Fold => "fold", PokerAction.Check => "check", PokerAction.Call => "call",
        PokerAction.Raise => "raise", PokerAction.AllIn => "all_in",
        _ => "unknown"
    };

    private static int? AmountOf(PokerAction a) => a is PokerAction.Raise r ? r.Amount : null;
}
```

- [ ] **Step 3: Run tests, confirm pass**

- [ ] **Step 4: Commit**

```pwsh
git add -A
git commit -m "feat(orchestrator): turn runner with retry + forced-default behavior"
```

### Task 3.6: Battle orchestrator (full match loop)

**Files:**
- Create: `src/AgentBattle.Orchestrator/BattleOrchestrator.cs`
- Create: `tests/AgentBattle.Orchestrator.Tests/BattleOrchestratorTests.cs`

- [ ] **Step 1: Define the public entry**

```csharp
namespace AgentBattle.Orchestrator;

public sealed class BattleOrchestrator(
    AgentBattle.Orchestrator.Recording.IBattleEventSink sink,
    System.TimeProvider time)
{
    public async System.Threading.Tasks.Task RunAsync(
        AgentBattle.Domain.Battles.BattleConfig config,
        IReadOnlyDictionary<string, AgentBattle.Domain.Battles.AgentProfile> profilesById,
        System.Func<AgentBattle.Domain.Battles.AgentProfile, AgentBattle.Orchestrator.Agents.IAgentClient> agentClientFactory,
        System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<AgentBattle.Orchestrator.Mcp.McpGameClient>> mcpClientFactory,
        System.Threading.CancellationToken ct = default)
    {
        // 1. Initialize MCP client, configure game
        // 2. Build AgentSession per seat (with system prompt and tool definitions)
        // 3. Emit battle_started
        // 4. For hand 1..config.Hands:
        //      Emit hand_started, hole_cards_dealt (after StartHand)
        //      Loop until showdown:
        //         For current seat, call TurnRunner
        //         Re-check street; if street advanced, emit community_dealt
        //      Emit showdown, hand_ended
        // 5. Emit battle_ended
    }
}
```

- [ ] **Step 2: Write integration test using in-memory fakes**

This is the most important single test in the entire codebase. It runs a 5-hand mini-battle between three stubbed agents whose scripted moves trace through the engine and assert that the JSONL ends in `battle_ended` and chip totals are conserved.

```csharp
[Fact]
public async Task Runs_a_short_match_and_emits_complete_event_stream()
{
    // - Use a real PokerGame wrapped in a fake McpGameClient that just calls the in-process game
    //   (skip stdio entirely for this test)
    // - Use a stub IAgentClient that always replies "I check." + check tool call
    //   (when check illegal, replies "I call." + call tool call)
    // - Run a 5-hand battle
    // - Read back the JSONL: assert event ordering, presence of battle_started + battle_ended,
    //   chip-conservation invariant on every hand_ended.
}
```

- [ ] **Step 3: Implement `BattleOrchestrator` to make the test pass**

Pseudocode (fill out using existing types from M2 and Task 3.5):

```
sink.Write(battle_started)
mcp = await mcpClientFactory()
await mcp.ConfigureGame(seats, stacks, names, sb, bb, button=0, seed=Random)

sessions = config.Seats.ToDictionary(s => s.Seat, s => new AgentSession(profiles[s.Agent], ...))
runner = new TurnRunner(sink, time)

for hand = 1..config.Hands:
    button = (button + 1) % activeSeats
    await mcp.StartHandAsync()
    sink.Write(hand_started with current button/sb/bb/inactive)
    state0 = await mcp.GetMyStateAsync(seats[0])  // we just need to read what was dealt; alternative: have MCP emit a reveal call
    sink.Write(hole_cards_dealt — built from state0... actually need a way to get reveals;
              EITHER expose a god-view tool on the MCP server callable only by orchestrator,
              OR record each seat's deal as we observe it via GetMyStateAsync per seat at the start)

    prevStreet = Preflop
    while state.Street != Showdown:
        await runner.RunOneTurnAsync(state.CurrentSeat, sessions[state.CurrentSeat], ...)
        state = await mcp.GetMyStateAsync(state.CurrentSeat)
        if state.Street != prevStreet:
            sink.Write(community_dealt for the new street)
            prevStreet = state.Street

    result = await mcp.ResolveShowdownAsync()
    sink.Write(showdown from result.Winners + result.Reveals)
    sink.Write(hand_ended with result.Stacks)

sink.Write(battle_ended with final stacks + ranking)
```

**Note on hole-card capture:** the orchestrator needs to log everyone's hole cards once at hand start (so the replay viewer's god-view works). Add a dedicated MCP tool `god_view_reveal()` that returns `Dictionary<int, Card[]>` — its very name flags that it should never be exposed to agents. The orchestrator's MCP client wraps this; the agent's tool list never includes it.

Add this to `PokerTools.cs`:

```csharp
[McpServerTool, Description("Orchestrator-only: returns every seat's hole cards for logging.")]
public static string GodViewReveal() => Serialize(Require()._hole);  // requires exposing _hole or wrapper
```

(Make `_hole` accessible via an internal helper method `IReadOnlyDictionary<int, IReadOnlyList<Card>> CurrentHoleCards()`.)

- [ ] **Step 4: Run integration test, iterate until it passes**

- [ ] **Step 5: Commit**

```pwsh
git add -A
git commit -m "feat(orchestrator): full battle orchestrator with hand-by-hand event emission"
```

### Task 3.7: Battle runner CLI

**Files:**
- Create: `src/AgentBattle.BattleRunner/AgentBattle.BattleRunner.csproj`
- Create: `src/AgentBattle.BattleRunner/Program.cs`
- Create: `src/AgentBattle.BattleRunner/Config/ConfigLoader.cs`
- Create: `configs/sample-poker.yaml`
- Create: `agents/sample-stub.yaml`

- [ ] **Step 1: Scaffold and add packages**

```pwsh
dotnet new console -n AgentBattle.BattleRunner -o src/AgentBattle.BattleRunner
dotnet sln add src/AgentBattle.BattleRunner/AgentBattle.BattleRunner.csproj
dotnet add src/AgentBattle.BattleRunner reference src/AgentBattle.Domain
dotnet add src/AgentBattle.BattleRunner reference src/AgentBattle.Orchestrator
dotnet add src/AgentBattle.BattleRunner package YamlDotNet
dotnet add src/AgentBattle.BattleRunner package System.CommandLine
```

- [ ] **Step 2: Implement `ConfigLoader.cs`**

```csharp
using AgentBattle.Domain.Battles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentBattle.BattleRunner.Config;

public static class ConfigLoader
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static BattleConfig LoadBattle(string path)
        => _yaml.Deserialize<BattleConfig>(System.IO.File.ReadAllText(path));

    public static AgentProfile LoadAgent(string path)
        => _yaml.Deserialize<AgentProfile>(System.IO.File.ReadAllText(path));

    public static IReadOnlyDictionary<string, AgentProfile> LoadAllAgentsIn(string agentsDir)
    {
        var dict = new Dictionary<string, AgentProfile>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var f in System.IO.Directory.EnumerateFiles(agentsDir, "*.yaml"))
        {
            var profile = LoadAgent(f);
            dict[profile.Id] = profile;
        }
        return dict;
    }
}
```

- [ ] **Step 3: Implement `Program.cs`** — CLI with one subcommand: `battle run --config <path> [--agents-dir <path>] [--out <path>]`

```csharp
using System.CommandLine;
using AgentBattle.BattleRunner.Config;
using AgentBattle.Orchestrator;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Mcp;
using AgentBattle.Orchestrator.Recording;

var configOpt    = new Option<string>("--config")    { IsRequired = true };
var agentsDirOpt = new Option<string>("--agents-dir") { IsRequired = false, };
agentsDirOpt.SetDefaultValue("agents");
var outOpt       = new Option<string>("--out")       { IsRequired = false };
outOpt.SetDefaultValue("battles");
var mcpExeOpt    = new Option<string>("--mcp-server-exe") { IsRequired = false };
mcpExeOpt.SetDefaultValue("AgentBattle.Poker.Mcp"); // resolved via PATH or `dotnet exec`

var runCmd = new Command("run", "Run a battle from a config file") { configOpt, agentsDirOpt, outOpt, mcpExeOpt };
runCmd.SetHandler(async (string configPath, string agentsDir, string outDir, string mcpExe) =>
{
    var config = ConfigLoader.LoadBattle(configPath);
    var profiles = ConfigLoader.LoadAllAgentsIn(agentsDir);

    System.IO.Directory.CreateDirectory(outDir);
    var battleId = System.Guid.NewGuid().ToString("N")[..8];
    var ts = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHHmm");
    var outPath = System.IO.Path.Combine(outDir, $"{ts}-{battleId}.jsonl");

    await using var sink = new JsonlEventSink(outPath);
    var http = new System.Net.Http.HttpClient();

    IAgentClient ClientFor(AgentBattle.Domain.Battles.AgentProfile p)
    {
        var key = string.IsNullOrEmpty(p.ApiKeyEnv) ? "" : (System.Environment.GetEnvironmentVariable(p.ApiKeyEnv) ?? "");
        return new OpenAiCompatibleAgent(http, p.BaseUrl, p.Model, key, p.Temperature, p.MaxTokens);
    }

    var orchestrator = new BattleOrchestrator(sink, System.TimeProvider.System);
    await orchestrator.RunAsync(config, profiles, ClientFor, ct => McpGameClient.SpawnAsync(mcpExe, ct));
    System.Console.WriteLine($"Battle complete: {outPath}");
}, configOpt, agentsDirOpt, outOpt, mcpExeOpt);

var battleCmd = new Command("battle") { runCmd };
var root = new RootCommand("AgentBattle CLI") { battleCmd };
return await root.InvokeAsync(args);
```

- [ ] **Step 4: Write sample config files**

`configs/sample-poker.yaml`:
```yaml
game: poker-6max
hands: 5
starting_stack: 1000
blinds: { small: 10, big: 20 }
seats:
  - { seat: 0, agent: stub-checker }
  - { seat: 1, agent: stub-checker }
  - { seat: 2, agent: stub-checker }
```

`agents/stub-checker.yaml`:
```yaml
id: stub-checker
display_name: StubChecker
base_url: http://localhost:9999/v1   # placeholder; real run requires a working endpoint
model: stub
api_key_env: NONE
temperature: 0.0
max_tokens: 200
timeout_seconds: 10
persona_prompt: |
  Always check when possible, otherwise call.
```

- [ ] **Step 5: Smoke run** — won't fully work without a real OpenAI-compatible endpoint up, but should at least parse the config and spawn the MCP server. Verify:

```pwsh
dotnet build
dotnet run --project src/AgentBattle.BattleRunner -- battle run --config configs/sample-poker.yaml --agents-dir agents
```

Expected: meaningful error from the agent HTTP call (`localhost:9999` not running) — *not* a crash from config parsing or MCP spawning.

- [ ] **Step 6: Commit**

```pwsh
git add -A
git commit -m "feat(runner): CLI to run a battle from YAML configs"
```

### Task 3.8: Real-endpoint smoke test against local Ollama

This is M4 from the milestone map — a manual checkpoint that we can run an actual end-to-end battle.

**Files:**
- Create: `agents/ollama-llama3.yaml`
- Create: `configs/poker-3p-ollama.yaml`

- [ ] **Step 1: Ensure Ollama is running locally**

```pwsh
ollama pull llama3
ollama serve   # if not already running
```

- [ ] **Step 2: Create the Ollama agent profile** at `agents/ollama-llama3.yaml`:

```yaml
id: ollama-llama3
display_name: Llama-3 (Ollama)
base_url: http://localhost:11434/v1
model: llama3
api_key_env: NONE
temperature: 0.7
max_tokens: 1500
timeout_seconds: 60
persona_prompt: |
  You like to mix up your play. Don't be afraid to bluff occasionally.
```

- [ ] **Step 3: Create the 3-player Ollama-vs-Ollama-vs-Ollama config**

```yaml
game: poker-6max
hands: 10
starting_stack: 1000
blinds: { small: 10, big: 20 }
seats:
  - { seat: 0, agent: ollama-llama3 }
  - { seat: 1, agent: ollama-llama3 }
  - { seat: 2, agent: ollama-llama3 }
```

- [ ] **Step 4: Run**

```pwsh
dotnet run --project src/AgentBattle.BattleRunner -- battle run --config configs/poker-3p-ollama.yaml
```

Expected: process runs for several minutes; produces a JSONL file ending in a `battle_ended` event. Inspect the JSONL — look for thoughts, rejections (likely several), and that chip totals are conserved per hand.

- [ ] **Step 5: Commit the configs (not the JSONL output)**

```pwsh
git add agents/ollama-llama3.yaml configs/poker-3p-ollama.yaml
git commit -m "feat(configs): sample Ollama 3-player battle config"
```

---

# Milestone 4 — Web viewer (battle list + replay)

### Task 4.1: ASP.NET Razor Pages scaffold + battle archive service

**Files:**
- Create: `src/AgentBattle.Web/AgentBattle.Web.csproj`
- Create: `src/AgentBattle.Web/Program.cs`
- Create: `src/AgentBattle.Web/appsettings.json`
- Create: `src/AgentBattle.Web/Services/BattleArchive.cs`
- Create: `src/AgentBattle.Web/Services/AgentRegistry.cs`
- Create: `src/AgentBattle.Web/Pages/_ViewStart.cshtml`, `_ViewImports.cshtml`, `Shared/_Layout.cshtml`
- Create: `src/AgentBattle.Web/Pages/Index.cshtml(.cs)`
- Create: `tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj`
- Create: `tests/AgentBattle.Web.Tests/Services/BattleArchiveTests.cs`

- [ ] **Step 1: Scaffold**

```pwsh
dotnet new webapp -n AgentBattle.Web -o src/AgentBattle.Web
dotnet new xunit -n AgentBattle.Web.Tests -o tests/AgentBattle.Web.Tests
dotnet sln add src/AgentBattle.Web/AgentBattle.Web.csproj
dotnet sln add tests/AgentBattle.Web.Tests/AgentBattle.Web.Tests.csproj
dotnet add src/AgentBattle.Web reference src/AgentBattle.Domain
dotnet add src/AgentBattle.Web package YamlDotNet
dotnet add tests/AgentBattle.Web.Tests reference src/AgentBattle.Web
dotnet add tests/AgentBattle.Web.Tests package FluentAssertions
```

Delete `Pages/Privacy.cshtml(.cs)`. Keep `_Layout.cshtml` and replace the body with a minimal nav: links to `/`, `/agents`.

- [ ] **Step 2: Configure paths in `appsettings.json`**

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "Paths": {
    "BattlesDirectory": "../../battles",
    "AgentsDirectory":  "../../agents"
  }
}
```

(Adjust relative paths to point at the solution-root `battles/` and `agents/` directories. In `Program.cs`, resolve to absolute paths using `Path.GetFullPath` relative to `ContentRootPath`.)

- [ ] **Step 3: Write failing test for `BattleArchive.ListBattles`**

```csharp
using AgentBattle.Web.Services;
using FluentAssertions;
using Xunit;

namespace AgentBattle.Web.Tests.Services;

public class BattleArchiveTests
{
    [Fact]
    public async Task ListBattles_reads_summary_from_first_and_last_events()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        var path = System.IO.Path.Combine(dir, "2026-05-13T1800-abc12345.jsonl");
        await System.IO.File.WriteAllLinesAsync(path,
        [
            """{"t":"battle_started","ts":"2026-05-13T18:00:00Z","battle_id":"abc12345","config_snapshot":"{}","agents":[{"seat":0,"id":"a","display_name":"A"},{"seat":1,"id":"b","display_name":"B"}]}""",
            """{"t":"battle_ended","ts":"2026-05-13T18:42:00Z","final_stacks":{"0":1200,"1":800},"ranking":[{"seat":0,"chips":1200,"agent_id":"a"},{"seat":1,"chips":800,"agent_id":"b"}]}"""
        ]);
        var archive = new BattleArchive(dir);
        var summaries = await archive.ListBattlesAsync();
        summaries.Should().HaveCount(1);
        summaries[0].BattleId.Should().Be("abc12345");
        summaries[0].AgentDisplayNames.Should().BeEquivalentTo(new[] { "A", "B" });
        summaries[0].WinnerAgentId.Should().Be("a");
        summaries[0].IsComplete.Should().BeTrue();
    }
}
```

- [ ] **Step 4: Implement `BattleArchive.cs`**

```csharp
using System.Text.Json;
using AgentBattle.Domain.Battles;
using AgentBattle.Domain.Json;

namespace AgentBattle.Web.Services;

public sealed record BattleSummary(string BattleId, string FilePath, System.DateTimeOffset StartedAt, IReadOnlyList<string> AgentDisplayNames, string? WinnerAgentId, bool IsComplete);

public sealed class BattleArchive(string battlesDir)
{
    public async System.Threading.Tasks.Task<IReadOnlyList<BattleSummary>> ListBattlesAsync(System.Threading.CancellationToken ct = default)
    {
        if (!System.IO.Directory.Exists(battlesDir)) return [];
        var summaries = new List<BattleSummary>();
        foreach (var file in System.IO.Directory.EnumerateFiles(battlesDir, "*.jsonl"))
        {
            var summary = await SummarizeAsync(file, ct);
            if (summary != null) summaries.Add(summary);
        }
        return summaries.OrderByDescending(s => s.StartedAt).ToArray();
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<BattleEvent>> LoadEventsAsync(string battleId, System.Threading.CancellationToken ct = default)
    {
        var file = System.IO.Directory.EnumerateFiles(battlesDir, "*.jsonl").FirstOrDefault(p => p.Contains(battleId));
        if (file == null) return [];
        var events = new List<BattleEvent>();
        await foreach (var line in System.IO.File.ReadLinesAsync(file, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var e = JsonSerializer.Deserialize<BattleEvent>(line, BattleEventJsonOptions.Default);
            if (e != null) events.Add(e);
        }
        return events;
    }

    private static async System.Threading.Tasks.Task<BattleSummary?> SummarizeAsync(string file, System.Threading.CancellationToken ct)
    {
        BattleEvent.BattleStarted? started = null;
        BattleEvent.BattleEnded? ended = null;
        await foreach (var line in System.IO.File.ReadLinesAsync(file, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var e = JsonSerializer.Deserialize<BattleEvent>(line, BattleEventJsonOptions.Default);
            switch (e)
            {
                case BattleEvent.BattleStarted s: started = s; break;
                case BattleEvent.BattleEnded x:   ended = x; break;
            }
        }
        if (started == null) return null;
        string? winner = null;
        if (ended != null && ended.Ranking.Count > 0)
            winner = ended.Ranking.OrderByDescending(r => r.Chips).First().AgentId;
        return new BattleSummary(
            BattleId: started.BattleId,
            FilePath: file,
            StartedAt: started.Ts,
            AgentDisplayNames: started.Agents.Select(a => a.DisplayName).ToArray(),
            WinnerAgentId: winner,
            IsComplete: ended != null);
    }
}
```

- [ ] **Step 5: Register services in `Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var battlesDir = System.IO.Path.GetFullPath(cfg["Paths:BattlesDirectory"]!, builder.Environment.ContentRootPath);
    return new AgentBattle.Web.Services.BattleArchive(battlesDir);
});
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var agentsDir = System.IO.Path.GetFullPath(cfg["Paths:AgentsDirectory"]!, builder.Environment.ContentRootPath);
    return new AgentBattle.Web.Services.AgentRegistry(agentsDir);
});

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();
```

- [ ] **Step 6: Implement `AgentRegistry.cs`** (mirror of `BattleArchive`, reads `*.yaml` agent profiles)

```csharp
using AgentBattle.Domain.Battles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentBattle.Web.Services;

public sealed class AgentRegistry(string agentsDir)
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties().Build();

    public IReadOnlyList<AgentProfile> List()
    {
        if (!System.IO.Directory.Exists(agentsDir)) return [];
        return System.IO.Directory.EnumerateFiles(agentsDir, "*.yaml")
            .Select(p => _yaml.Deserialize<AgentProfile>(System.IO.File.ReadAllText(p)))
            .OrderBy(a => a.DisplayName)
            .ToArray();
    }
}
```

- [ ] **Step 7: Implement `Pages/Index.cshtml.cs` and `Index.cshtml`**

```csharp
// Index.cshtml.cs
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages;

public class IndexModel(BattleArchive archive) : PageModel
{
    public IReadOnlyList<BattleSummary> Battles { get; private set; } = [];
    public async System.Threading.Tasks.Task OnGetAsync() => Battles = await archive.ListBattlesAsync();
}
```

```html
@* Index.cshtml *@
@page
@model AgentBattle.Web.Pages.IndexModel
@{ ViewData["Title"] = "Battles"; }

<h1>Recent battles</h1>
@if (Model.Battles.Count == 0)
{
    <p>No battles recorded yet. Run one with <code>dotnet run --project src/AgentBattle.BattleRunner -- battle run --config configs/sample-poker.yaml</code>.</p>
}
else
{
    <table class="battles">
      <thead><tr><th>Started</th><th>Agents</th><th>Winner</th><th></th></tr></thead>
      <tbody>
        @foreach (var b in Model.Battles)
        {
          <tr>
            <td>@b.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")</td>
            <td>@string.Join(", ", b.AgentDisplayNames)</td>
            <td>@(b.IsComplete ? b.WinnerAgentId ?? "—" : "(incomplete)")</td>
            <td><a asp-page="/Battles/Replay" asp-route-id="@b.BattleId">Watch</a></td>
          </tr>
        }
      </tbody>
    </table>
}
```

- [ ] **Step 8: Run tests, run app**

```pwsh
dotnet test tests/AgentBattle.Web.Tests
dotnet run --project src/AgentBattle.Web
```

Expected: tests pass; web app renders the list page (empty initially, populated after running a battle).

- [ ] **Step 9: Commit**

```pwsh
git add -A
git commit -m "feat(web): battle archive service and list page"
```

### Task 4.2: Replay page with raw JSONL endpoint

**Files:**
- Create: `src/AgentBattle.Web/Pages/Battles/Replay.cshtml(.cs)`
- Create: `src/AgentBattle.Web/wwwroot/js/replay.js`
- Create: `src/AgentBattle.Web/wwwroot/css/site.css` (extend if exists)

- [ ] **Step 1: Implement the page model**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages.Battles;

public class ReplayModel(BattleArchive archive) : PageModel
{
    public string BattleId { get; private set; } = "";
    public void OnGet(string id) => BattleId = id;

    public async System.Threading.Tasks.Task<IActionResult> OnGetEventsAsync(string id)
    {
        var events = await archive.LoadEventsAsync(id, HttpContext.RequestAborted);
        var json = System.Text.Json.JsonSerializer.Serialize(events, AgentBattle.Domain.Json.BattleEventJsonOptions.Default);
        return Content(json, "application/json");
    }
}
```

`?handler=events` then exposes `/Battles/Replay/{id}?handler=events` which returns the JSONL as a JSON array.

- [ ] **Step 2: Replay page markup**

```html
@page "{id}"
@model AgentBattle.Web.Pages.Battles.ReplayModel
@{ ViewData["Title"] = $"Battle {Model.BattleId}"; }

<div x-data="replay('@Model.BattleId')" x-init="load()" class="replay">
  <header class="replay-header">
    <h1>Battle <code>@Model.BattleId</code></h1>
    <label>
      <input type="checkbox" x-model="godView" />
      God view (show all hole cards)
    </label>
  </header>

  <section class="poker-table" x-show="loaded">
    <template x-for="seat in seats" :key="seat.seat">
      <div class="seat" :class="{ 'is-current': seat.seat === currentSeat }">
        <div class="name" x-text="seat.displayName"></div>
        <div class="stack" x-text="'Stack: ' + seat.stack"></div>
        <div class="cards">
          <template x-for="card in cardsForSeat(seat.seat)" :key="card">
            <span class="card" x-text="card"></span>
          </template>
        </div>
        <div class="bet" x-text="seat.currentBet > 0 ? 'Bet: ' + seat.currentBet : ''"></div>
      </div>
    </template>
    <div class="community">
      <template x-for="card in community" :key="card">
        <span class="card" x-text="card"></span>
      </template>
      <div class="pot" x-text="'Pot: ' + pot"></div>
    </div>
  </section>

  <section class="controls">
    <button @click="prev()">⏮</button>
    <button @click="togglePlay()" x-text="playing ? '⏸' : '▶'"></button>
    <button @click="next()">⏭</button>
    <select x-model.number="speed">
      <option value="1">1×</option>
      <option value="2">2×</option>
      <option value="4">4×</option>
    </select>
    <input type="range" min="0" :max="events.length - 1" x-model.number="idx" />
    <span x-text="describeCurrent()"></span>
  </section>

  <aside class="thoughts">
    <template x-for="seat in seats" :key="seat.seat">
      <details>
        <summary x-text="seat.displayName + ' — turn ' + (lastThoughts[seat.seat]?.handNo ?? '—')"></summary>
        <p x-text="lastThoughts[seat.seat]?.text ?? '(no thoughts yet)'"></p>
      </details>
    </template>
  </aside>
</div>

<script src="~/lib/alpine.min.js" defer></script>
<script src="~/js/replay.js"></script>
```

(Vendor Alpine.js by downloading `alpine.min.js` to `wwwroot/lib/`. HTMX isn't needed for this page.)

- [ ] **Step 3: Implement `replay.js`** — Alpine state machine

```javascript
function replay(battleId) {
  return {
    battleId,
    events: [],
    idx: 0,
    playing: false,
    speed: 1,
    godView: true,
    loaded: false,

    // Derived state, rebuilt from events[0..idx]
    seats: [],
    community: [],
    pot: 0,
    currentSeat: null,
    holeCards: {},      // seat -> [card, card]
    lastThoughts: {},   // seat -> { handNo, text }
    _timer: null,

    async load() {
      const res = await fetch(`/Battles/Replay/${this.battleId}?handler=events`);
      this.events = await res.json();
      this.rebuild();
      this.loaded = true;
    },

    togglePlay() {
      this.playing = !this.playing;
      if (this.playing) this._tick();
      else clearTimeout(this._timer);
    },
    _tick() {
      if (!this.playing) return;
      if (this.idx < this.events.length - 1) {
        this.idx++;
        this.rebuild();
        this._timer = setTimeout(() => this._tick(), 800 / this.speed);
      } else {
        this.playing = false;
      }
    },
    prev() { if (this.idx > 0) { this.idx--; this.rebuild(); } },
    next() { if (this.idx < this.events.length - 1) { this.idx++; this.rebuild(); } },

    rebuild() {
      // Fold events[0..idx] into derived state.
      this.seats = [];
      this.community = [];
      this.pot = 0;
      this.currentSeat = null;
      this.holeCards = {};
      this.lastThoughts = {};
      for (let i = 0; i <= this.idx; i++) {
        const e = this.events[i];
        switch (e.t) {
          case 'battle_started':
            this.seats = e.agents.map(a => ({ seat: a.seat, displayName: a.display_name, stack: 1000, currentBet: 0, hasFolded: false }));
            break;
          case 'hand_started':
            this.community = []; this.pot = 0; this.holeCards = {};
            this.seats.forEach(s => { s.currentBet = 0; s.hasFolded = false; });
            break;
          case 'hole_cards_dealt':
            e.deals.forEach(d => this.holeCards[d.seat] = d.cards);
            break;
          case 'community_dealt':
            this.community.push(...e.cards);
            break;
          case 'agent_turn_started':
            this.currentSeat = e.seat;
            const snap = e.state_snapshot;
            // Update stacks/bets from state snapshot
            if (snap?.seats) snap.seats.forEach(s => {
              const local = this.seats.find(x => x.seat === s.seat);
              if (local) { local.stack = s.stack; local.currentBet = s.current_bet; local.hasFolded = s.has_folded; }
            });
            if (snap) this.pot = snap.pot;
            break;
          case 'agent_thoughts':
            this.lastThoughts[e.seat] = { handNo: e.hand_no, text: e.text };
            break;
          case 'hand_ended':
            for (const [seat, chips] of Object.entries(e.stacks)) {
              const local = this.seats.find(x => x.seat === Number(seat));
              if (local) local.stack = chips;
            }
            break;
          case 'showdown':
            // optionally annotate winners
            break;
        }
      }
    },

    cardsForSeat(seat) {
      if (this.godView) return this.holeCards[seat] ?? [];
      // Spectator mode: hide hole cards unless they were revealed at showdown earlier in playback.
      // For MVP simplicity: hide until showdown event for this hand has been processed.
      // (Implementation: check whether the most recent showdown event ≤ idx reveals this seat.)
      return ['🂠', '🂠'];
    },

    describeCurrent() {
      const e = this.events[this.idx];
      if (!e) return '';
      return `event ${this.idx + 1}/${this.events.length}: ${e.t}`;
    }
  };
}
```

- [ ] **Step 4: Add minimal CSS** in `wwwroot/css/site.css` for the poker table — six seats around an oval, community in the middle. ~50 lines of CSS. Keep it functional, not beautiful — we'll polish in M5.

- [ ] **Step 5: Manual run-through**

```pwsh
dotnet run --project src/AgentBattle.Web
```

Open `http://localhost:5000/`, click into a recorded battle. Verify play/pause/scrub work, thoughts panels update.

- [ ] **Step 6: Commit**

```pwsh
git add -A
git commit -m "feat(web): replay page with Alpine-driven scrubbable playback"
```

### Task 4.3: Spectator-mode toggle (hide hole cards until showdown)

The `cardsForSeat` function above always reveals when god-view is on. For spectator mode, we need to fold the events incrementally and know which seats have been revealed *up to the current index* via a `showdown` event in the same hand.

**Files:**
- Modify: `src/AgentBattle.Web/wwwroot/js/replay.js`

- [ ] **Step 1: Add `revealedAtIdx` map** — during `rebuild()`, track which seats are currently revealed for the current hand:

```javascript
// in state:
revealedSeats: new Set(),  // seats whose hole cards are visible to spectators

// in rebuild(), after this.holeCards reset on hand_started:
case 'hand_started':
  this.community = []; this.pot = 0; this.holeCards = {}; this.revealedSeats = new Set();
  // ...

case 'showdown':
  e.reveals.forEach(r => this.revealedSeats.add(r.seat));
  break;

// also: when a seat folds, they're not revealed (they don't show cards)
// (No event change needed — agent_action with action=fold updates seats[].hasFolded; cards stay hidden.)
```

Update `cardsForSeat`:

```javascript
cardsForSeat(seat) {
  if (this.godView) return this.holeCards[seat] ?? [];
  if (this.revealedSeats.has(seat)) return this.holeCards[seat] ?? [];
  return ['🂠', '🂠'];
}
```

- [ ] **Step 2: Manual verify** — toggle the god-view checkbox during playback; cards should hide everywhere except at showdown reveals.

- [ ] **Step 3: Commit**

```pwsh
git add -A
git commit -m "feat(web): spectator-mode toggle hides hole cards until showdown"
```

### Task 4.4: Agents page

**Files:**
- Create: `src/AgentBattle.Web/Pages/Agents/Index.cshtml(.cs)`

- [ ] **Step 1: Page model**

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;
using AgentBattle.Domain.Battles;

namespace AgentBattle.Web.Pages.Agents;

public class IndexModel(AgentRegistry registry) : PageModel
{
    public IReadOnlyList<AgentProfile> Agents { get; private set; } = [];
    public void OnGet() => Agents = registry.List();
}
```

- [ ] **Step 2: View**

```html
@page
@model AgentBattle.Web.Pages.Agents.IndexModel
@{ ViewData["Title"] = "Agents"; }

<h1>Registered agents</h1>
<table class="agents">
  <thead><tr><th>ID</th><th>Display name</th><th>Model</th><th>Endpoint</th></tr></thead>
  <tbody>
    @foreach (var a in Model.Agents)
    {
      <tr>
        <td><code>@a.Id</code></td>
        <td>@a.DisplayName</td>
        <td>@a.Model</td>
        <td><code>@a.BaseUrl</code></td>
      </tr>
    }
  </tbody>
</table>
```

- [ ] **Step 3: Add link in `_Layout.cshtml`** to the new page.

- [ ] **Step 4: Manual verify, commit**

```pwsh
git add -A
git commit -m "feat(web): agents page lists registered profiles"
```

---

# Milestone 5 — Polish

### Task 5.1: Card and chip animations + table layout polish

**Files:**
- Modify: `src/AgentBattle.Web/wwwroot/css/site.css`
- Optionally: `src/AgentBattle.Web/wwwroot/js/poker-table.js` (if you want layout helpers)

- [ ] **Step 1: Hand-author the poker table layout**

Goals:
- Six seats positioned around an ellipse (use `transform: rotate()` + `translate()` per seat).
- Community cards centered.
- Pot above community cards.
- Current-turn seat gets a glow.

CSS sketch (~100 lines): position six `.seat` divs with `position: absolute; top/left` for each, or use CSS custom properties indexed by `--i`. Add `.seat.is-current { box-shadow: 0 0 12px gold; }`.

- [ ] **Step 2: Card animations**

Use CSS transitions:
- Card enter (new community card): `transform: scale(0) → scale(1)` over 250ms.
- Card flip (showdown reveal): `transform: rotateY(0 → 180deg)`, with the back face on one side.
- Chip movement (pot win): brief animation of a `.chip` element from pot to winner's seat. Keep simple — a single flying chip is fine.

- [ ] **Step 3: Manual polish pass**

Open one full battle replay, watch start to finish, fix anything that looks broken.

- [ ] **Step 4: Commit**

```pwsh
git add -A
git commit -m "polish(web): poker table layout, card and chip animations"
```

### Task 5.2: README update — quick-start

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Replace the placeholder Quick Start section with a real one:**

```markdown
## Quick start

Prerequisites: .NET 10, optionally Ollama for local models.

# 1. Build
dotnet build

# 2. Start a local Ollama if you want to run against it
ollama pull llama3 && ollama serve

# 3. Run a battle
dotnet run --project src/AgentBattle.BattleRunner -- battle run --config configs/poker-3p-ollama.yaml

# 4. Watch the replay
dotnet run --project src/AgentBattle.Web
# Open http://localhost:5000
```

- [ ] **Step 2: Commit**

```pwsh
git add README.md
git commit -m "docs: README quick-start for first-run experience"
```

---

## Self-review

Walking the spec against the plan:

| Spec section | Covered by |
|---|---|
| §2 Product surface | M4 (list, replay, agents pages), M5 (polish) |
| §3 Locked decisions | Reflected in task specifics (50 hands, 6-max, retry counts, etc.) |
| §4.1 Solution layout | M1 + each task creates its named project |
| §4.2 Data and control flow | M3 (orchestrator) wires the whole flow |
| §4.3 MCP integration model | Tasks 2.9, 3.4 |
| §4.4 Turn loop | Task 3.5 |
| §4.5 Per-agent prompting | Task 3.3 (`PromptBuilder`) |
| §5 Battle event schema | Task 2.3 (`BattleEvent`), Task 3.1 (sink) |
| §6 Agent profile + battle config | Tasks 2.3, 3.7 |
| §7 Web frontend | M4 + M5 |
| §8 Error handling | Task 3.5 (retry behavior); Task 4.1 (incomplete battle marking) |
| §9 Out of scope | Honored — no leaderboards, live spectating, multi-game support |

No placeholders remain in the plan (no `TBD`/`TODO` patterns); type names are consistent across tasks (`PokerAction`, `PokerState`, `BattleEvent.*`, `LegalActions`, `AgentSession`, `IAgentClient`). The `OneOf` package mention in Task 3.3 was replaced with the tuple-return variant before committing.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-13-agentbattle-poker-mvp.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for a multi-day build where you want each task reviewed in isolation.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batching tasks with checkpoints for review. Best if you want to ride along and steer.

Which approach?

