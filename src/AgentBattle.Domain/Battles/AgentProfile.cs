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
