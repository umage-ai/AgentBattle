using System.CommandLine;
using AgentBattle.BattleRunner.Config;
using AgentBattle.Domain.Battles;
using AgentBattle.Orchestrator;
using AgentBattle.Orchestrator.Agents;
using AgentBattle.Orchestrator.Mcp;
using AgentBattle.Orchestrator.Recording;

// System.CommandLine 2.0.8 uses the new immutable-style API: options live on Command.Options,
// values are read via parseResult.GetValue(option), and actions are registered with SetAction.

var configOpt = new Option<string>("--config")
{
    Description = "Path to the battle YAML config.",
    Required = true,
};

var agentsDirOpt = new Option<string>("--agents-dir")
{
    Description = "Directory containing agent profile YAML files.",
    DefaultValueFactory = _ => "agents",
};

var outOpt = new Option<string>("--out")
{
    Description = "Directory to write the battle JSONL into.",
    DefaultValueFactory = _ => "battles",
};

var mcpExeOpt = new Option<string>("--mcp-server-exe")
{
    Description = "Path to the AgentBattle.Poker.Mcp executable used as the game backend.",
    DefaultValueFactory = _ => "src/AgentBattle.Poker.Mcp/bin/Debug/net10.0/AgentBattle.Poker.Mcp.exe",
};

var runCmd = new Command("run", "Run a battle from a config file.");
runCmd.Options.Add(configOpt);
runCmd.Options.Add(agentsDirOpt);
runCmd.Options.Add(outOpt);
runCmd.Options.Add(mcpExeOpt);

runCmd.SetAction(async (parseResult, ct) =>
{
    var configPath = parseResult.GetValue(configOpt)!;
    var agentsDir = parseResult.GetValue(agentsDirOpt)!;
    var outDir = parseResult.GetValue(outOpt)!;
    var mcpExe = parseResult.GetValue(mcpExeOpt)!;

    // Normalize the MCP server path to an absolute, platform-native form. On Windows the
    // MCP stdio transport invokes the command through `cmd /c`, and forward slashes in a
    // relative path cause cmd to interpret the first segment as a command name. Absolute
    // path with native separators avoids both issues.
    mcpExe = System.IO.Path.GetFullPath(mcpExe);

    var config = ConfigLoader.LoadBattle(configPath);
    var profiles = ConfigLoader.LoadAllAgentsIn(agentsDir);

    System.IO.Directory.CreateDirectory(outDir);
    var battleId = System.Guid.NewGuid().ToString("N")[..8];
    var ts = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHHmm");
    var outPath = System.IO.Path.Combine(outDir, $"{ts}-{battleId}.jsonl");

    await using var sink = new JsonlEventSink(outPath);
    using var http = new System.Net.Http.HttpClient();

    IAgentClient ClientFor(AgentProfile p)
    {
        // ApiKeyEnv == "NONE" (or empty) is the convention for local endpoints like Ollama
        // where Bearer auth is not required.
        var key = string.IsNullOrEmpty(p.ApiKeyEnv) || string.Equals(p.ApiKeyEnv, "NONE", System.StringComparison.OrdinalIgnoreCase)
            ? ""
            : (System.Environment.GetEnvironmentVariable(p.ApiKeyEnv) ?? "");
        return new OpenAiCompatibleAgent(http, p.BaseUrl, p.Model, key, p.Temperature, p.MaxTokens);
    }

    await BattleOrchestrator.RunAsync(
        config,
        profiles,
        agentClientFactory: ClientFor,
        mcpFactory: token => McpGameClient.SpawnAsync(mcpExe, System.Array.Empty<string>(), token),
        sink: sink,
        time: System.TimeProvider.System,
        battleId: battleId,
        ct: ct);

    System.Console.WriteLine($"Battle complete: {outPath}");
    return 0;
});

var battleCmd = new Command("battle", "Battle operations.");
battleCmd.Subcommands.Add(runCmd);

var root = new RootCommand("AgentBattle CLI");
root.Subcommands.Add(battleCmd);

return await root.Parse(args).InvokeAsync();
