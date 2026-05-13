using System.Text.Json;
using AgentBattle.Domain.Cards;
using AgentBattle.Domain.Json;
using AgentBattle.Domain.Poker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentBattle.Orchestrator.Mcp;

/// <summary>
/// Thin wrapper around an MCP stdio client connected to <c>AgentBattle.Poker.Mcp</c>.
/// Spawns the server as a child process and exposes each poker tool as a typed C# method.
/// Tool responses come back as JSON text content and are deserialized using the same
/// <see cref="BattleEventJsonOptions.Default"/> options the server used to produce them.
/// </summary>
public sealed class McpGameClient : System.IAsyncDisposable
{
    private readonly McpClient _client;

    private McpGameClient(McpClient client) { _client = client; }

    /// <summary>
    /// Spawn the poker MCP server as a child process and connect via stdio.
    /// </summary>
    /// <param name="serverCommand">Executable to launch (e.g. <c>"dotnet"</c> or a full path to the server exe).</param>
    /// <param name="serverArgs">Arguments to pass to the server process.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async System.Threading.Tasks.Task<McpGameClient> SpawnAsync(
        string serverCommand,
        IReadOnlyList<string> serverArgs,
        System.Threading.CancellationToken ct = default)
    {
        var options = new StdioClientTransportOptions
        {
            Command = serverCommand,
            Arguments = serverArgs.ToList(),
            Name = "AgentBattle.Poker.Mcp",
        };
        var transport = new StdioClientTransport(options);
        var client = await McpClient.CreateAsync(transport, clientOptions: null, loggerFactory: null, cancellationToken: ct);
        return new McpGameClient(client);
    }

    public async System.Threading.Tasks.Task ConfigureGameAsync(
        int[] seats, int[] startingStacks, string[] agentNames,
        int smallBlind, int bigBlind, int buttonSeat, int deckSeed,
        System.Threading.CancellationToken ct = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["seats"] = seats,
            ["startingStacks"] = startingStacks,
            ["agentNames"] = agentNames,
            ["smallBlind"] = smallBlind,
            ["bigBlind"] = bigBlind,
            ["buttonSeat"] = buttonSeat,
            ["deckSeed"] = deckSeed,
        };
        var text = await CallAsync("configure_game", args, ct);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException($"configure_game failed: {text}");
    }

    public async System.Threading.Tasks.Task<int> StartHandAsync(System.Threading.CancellationToken ct = default)
    {
        var text = await CallAsync("start_hand", new Dictionary<string, object?>(), ct);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException($"start_hand failed: {text}");
        return doc.RootElement.GetProperty("hand_no").GetInt32();
    }

    public async System.Threading.Tasks.Task<PokerState> GetMyStateAsync(int seat, System.Threading.CancellationToken ct = default)
    {
        var args = new Dictionary<string, object?> { ["seat"] = seat };
        var text = await CallAsync("get_my_state", args, ct);
        return JsonSerializer.Deserialize<PokerState>(text, BattleEventJsonOptions.Default)
               ?? throw new InvalidOperationException($"get_my_state returned null/empty payload: {text}");
    }

    public async System.Threading.Tasks.Task<(bool Ok, string? Error)> ApplyAsync(PokerAction action, System.Threading.CancellationToken ct = default)
    {
        var (toolName, args) = action switch
        {
            PokerAction.Fold f => ("fold", new Dictionary<string, object?> { ["seat"] = f.Seat }),
            PokerAction.Check c => ("check", new Dictionary<string, object?> { ["seat"] = c.Seat }),
            PokerAction.Call c => ("call", new Dictionary<string, object?> { ["seat"] = c.Seat }),
            PokerAction.Raise r => ("raise", new Dictionary<string, object?> { ["seat"] = r.Seat, ["amount"] = r.Amount }),
            PokerAction.AllIn a => ("all_in", new Dictionary<string, object?> { ["seat"] = a.Seat }),
            _ => throw new ArgumentOutOfRangeException(nameof(action), $"Unknown PokerAction: {action.GetType().Name}"),
        };
        var text = await CallAsync(toolName, args, ct);
        using var doc = JsonDocument.Parse(text);
        var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
        if (ok) return (true, null);
        var err = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() : text;
        return (false, err);
    }

    public async System.Threading.Tasks.Task<McpShowdownResult> ResolveShowdownAsync(System.Threading.CancellationToken ct = default)
    {
        var text = await CallAsync("resolve_showdown", new Dictionary<string, object?>(), ct);
        return JsonSerializer.Deserialize<McpShowdownResult>(text, BattleEventJsonOptions.Default)
               ?? throw new InvalidOperationException($"resolve_showdown returned null/empty payload: {text}");
    }

    public async System.Threading.Tasks.Task<IReadOnlyDictionary<int, IReadOnlyList<Card>>> GodViewRevealAsync(System.Threading.CancellationToken ct = default)
    {
        var text = await CallAsync("god_view_reveal", new Dictionary<string, object?>(), ct);
        // The server emits a Dictionary<int, List<Card>>. System.Text.Json serializes
        // non-string-keyed dictionaries as JSON objects with stringified keys, e.g.
        // {"0":["As","Kd"], "1":["Qs","Qh"]}. Deserialize via the same options.
        var parsed = JsonSerializer.Deserialize<Dictionary<int, List<Card>>>(text, BattleEventJsonOptions.Default)
                     ?? throw new InvalidOperationException($"god_view_reveal returned null/empty payload: {text}");
        return parsed.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Card>)kv.Value);
    }

    private async System.Threading.Tasks.Task<string> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        System.Threading.CancellationToken ct)
    {
        var nonNull = args.Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        var result = await _client.CallToolAsync(toolName, nonNull, progress: null, options: null, cancellationToken: ct);
        if (result.IsError == true)
        {
            var errText = ExtractText(result);
            throw new InvalidOperationException($"MCP tool '{toolName}' returned an error: {errText}");
        }
        return ExtractText(result);
    }

    private static string ExtractText(CallToolResult result)
    {
        if (result.Content is null) return string.Empty;
        // Concatenate all TextContentBlock entries; tool responses from our server emit a single string.
        var sb = new System.Text.StringBuilder();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock txt) sb.Append(txt.Text);
        }
        return sb.ToString();
    }

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}

/// <summary>
/// Mirror of the server-side <c>ShowdownResult</c> shape — the orchestrator can't reference
/// <c>AgentBattle.Poker.Mcp</c> directly, so we redeclare the wire types here. JSON layout
/// must stay in sync with <c>AgentBattle.Poker.Mcp.Engine.ShowdownResult</c>.
/// </summary>
public sealed record McpShowdownResult(
    IReadOnlyDictionary<int, int> Stacks,
    IReadOnlyList<McpPotWinner> Winners,
    IReadOnlyDictionary<int, IReadOnlyList<Card>> Reveals);

public sealed record McpPotWinner(int Seat, int Pot, string Description);
