using System.CommandLine;
using System.Text.Json;
using AgentBattle.Web.Services;

var battlesOpt     = new Option<string>("--battles-dir")  { Required = true, Description = "Directory of *.jsonl battle logs." };
var agentsOpt      = new Option<string>("--agents-dir")   { Required = true, Description = "Directory of *.yaml agent profiles (templates only — no keys)." };
var outOpt         = new Option<string>("--out-dir")      { Required = true, Description = "Directory to write generated data + copied battles into." };
var copyBattlesOpt = new Option<bool>("--copy-battles")   { DefaultValueFactory = _ => true, Description = "Copy *.jsonl files into <out-dir>/battles/ so the static site can fetch them." };

var root = new RootCommand("Generate JSON manifests + stats for the AgentBattle static site.");
root.Options.Add(battlesOpt);
root.Options.Add(agentsOpt);
root.Options.Add(outOpt);
root.Options.Add(copyBattlesOpt);

root.SetAction(async (parse, ct) =>
{
    var battlesDir = parse.GetValue(battlesOpt)!;
    var agentsDir  = parse.GetValue(agentsOpt)!;
    var outDir     = parse.GetValue(outOpt)!;
    var copyBattles = parse.GetValue(copyBattlesOpt);

    Directory.CreateDirectory(outDir);
    var dataDir = Path.Combine(outDir, "data");
    Directory.CreateDirectory(dataDir);

    var archive  = new BattleArchive(battlesDir);
    var registry = new AgentRegistry(agentsDir);
    var stats    = new StatsAggregator();

    Console.WriteLine($"Reading battles from {battlesDir}");
    var battles = await archive.ListBattlesAsync(ct);
    var agents  = registry.List();
    Console.WriteLine($"  {battles.Count} battles, {agents.Count} agent profiles");

    var profilesById = agents.ToDictionary(a => a.Id, a => a);
    var snapshot = stats.Build(battles, profilesById);

    var jsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // battles.json — strip FilePath (server-only path) and emit only display-relevant data.
    var battleManifest = battles.Select(b => new
    {
        b.BattleId,
        b.StartedAt,
        b.IsComplete,
        b.WinnerAgentId,
        b.StartingStack,
        AgentDisplayNames = b.AgentDisplayNames,
        File = Path.GetFileName(b.FilePath),
        SeatedAgents = b.SeatedAgents.Select(sa => new { sa.Seat, sa.Id, sa.DisplayName }),
        Ranking = b.Ranking.Select(r => new { r.Seat, r.Chips, r.AgentId }),
    }).ToArray();
    await File.WriteAllTextAsync(Path.Combine(dataDir, "battles.json"),
        JsonSerializer.Serialize(battleManifest, jsonOpts), ct);

    // agents.json — public template view; ApiKeyEnv (env var name) is dropped as an implementation detail.
    var agentManifest = agents.Select(a => new
    {
        a.Id,
        a.DisplayName,
        a.Model,
        a.BaseUrl,
        a.PersonaPrompt,
        Slug = ModelSlug.For(a.DisplayName),
    }).ToArray();
    await File.WriteAllTextAsync(Path.Combine(dataDir, "agents.json"),
        JsonSerializer.Serialize(agentManifest, jsonOpts), ct);

    // stats.json — direct serialisation of the snapshot.
    await File.WriteAllTextAsync(Path.Combine(dataDir, "stats.json"),
        JsonSerializer.Serialize(snapshot, jsonOpts), ct);

    if (copyBattles)
    {
        var battlesOut = Path.Combine(outDir, "battles");
        Directory.CreateDirectory(battlesOut);
        foreach (var b in battles)
        {
            var dest = Path.Combine(battlesOut, Path.GetFileName(b.FilePath));
            File.Copy(b.FilePath, dest, overwrite: true);
        }
        Console.WriteLine($"  copied {battles.Count} JSONL files to {battlesOut}");
    }

    Console.WriteLine($"Wrote data/battles.json, data/agents.json, data/stats.json under {outDir}");
});

return await root.Parse(args).InvokeAsync();
