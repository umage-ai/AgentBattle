using AgentBattle.Domain.Battles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentBattle.BattleRunner.Config;

/// <summary>
/// Loads <see cref="BattleConfig"/> and <see cref="AgentProfile"/> documents from YAML.
/// Uses internal mutable DTOs to bridge YamlDotNet's setter-based hydration model and
/// our positional immutable domain records.
/// </summary>
public static class ConfigLoader
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static BattleConfig LoadBattle(string path)
        => _yaml.Deserialize<BattleConfigDto>(System.IO.File.ReadAllText(path)).ToDomain();

    public static AgentProfile LoadAgent(string path)
        => _yaml.Deserialize<AgentProfileDto>(System.IO.File.ReadAllText(path)).ToDomain();

    public static IReadOnlyDictionary<string, AgentProfile> LoadAllAgentsIn(string agentsDir)
    {
        var dict = new Dictionary<string, AgentProfile>(System.StringComparer.OrdinalIgnoreCase);
        if (!System.IO.Directory.Exists(agentsDir)) return dict;
        foreach (var f in System.IO.Directory.EnumerateFiles(agentsDir, "*.yaml"))
        {
            var profile = LoadAgent(f);
            dict[profile.Id] = profile;
        }
        return dict;
    }
}
