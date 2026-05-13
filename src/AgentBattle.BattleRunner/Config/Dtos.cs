using AgentBattle.Domain.Battles;

namespace AgentBattle.BattleRunner.Config;

// YamlDotNet's default deserializer wants parameterless constructors and public setters.
// Our domain records are positional and immutable, so we deserialize into these mutable DTOs
// and then map across. Keeps the domain free of YAML-specific shape concerns.

internal sealed class BattleConfigDto
{
    public string Game { get; set; } = "";
    public int Hands { get; set; }
    public int StartingStack { get; set; }
    public BlindsConfigDto Blinds { get; set; } = new();
    public List<SeatAssignmentDto> Seats { get; set; } = new();

    public BattleConfig ToDomain() => new(
        Game,
        Hands,
        StartingStack,
        Blinds.ToDomain(),
        Seats.Select(s => s.ToDomain()).ToList());
}

internal sealed class BlindsConfigDto
{
    public int Small { get; set; }
    public int Big { get; set; }

    public BlindsConfig ToDomain() => new(Small, Big);
}

internal sealed class SeatAssignmentDto
{
    public int Seat { get; set; }
    public string Agent { get; set; } = "";

    public SeatAssignment ToDomain() => new(Seat, Agent);
}

internal sealed class AgentProfileDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKeyEnv { get; set; } = "";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1500;
    public int TimeoutSeconds { get; set; } = 60;
    public string PersonaPrompt { get; set; } = "";

    public AgentProfile ToDomain() => new(
        Id,
        DisplayName,
        BaseUrl,
        Model,
        ApiKeyEnv,
        Temperature,
        MaxTokens,
        TimeoutSeconds,
        PersonaPrompt);
}
