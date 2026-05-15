using AgentBattle.Domain.Battles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentBattle.Web.Services;

public sealed class AgentRegistry(string agentsDir)
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public IReadOnlyList<AgentProfile> List()
    {
        if (!System.IO.Directory.Exists(agentsDir)) return [];

        // The Domain AgentProfile is a positional record — YamlDotNet can't construct it directly.
        // Mirror the DTO pattern from BattleRunner.
        var profiles = new List<AgentProfile>();
        foreach (var p in System.IO.Directory.EnumerateFiles(agentsDir, "*.yaml"))
        {
            var dto = _yaml.Deserialize<AgentProfileDto>(System.IO.File.ReadAllText(p));
            if (dto != null)
                profiles.Add(new AgentProfile(
                    dto.Id ?? "", dto.DisplayName ?? dto.Id ?? "",
                    dto.BaseUrl ?? "", dto.Model ?? "", dto.ApiKeyEnv ?? "",
                    dto.Temperature, dto.MaxTokens, dto.TimeoutSeconds, dto.PersonaPrompt ?? ""));
        }
        return profiles.OrderBy(a => a.DisplayName).ToArray();
    }

    private sealed class AgentProfileDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? BaseUrl { get; set; }
        public string? Model { get; set; }
        public string? ApiKeyEnv { get; set; }
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1500;
        public int TimeoutSeconds { get; set; } = 60;
        public string? PersonaPrompt { get; set; }
    }
}
