using AgentBattle.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

string FindSolutionRoot(string start)
{
    var dir = new System.IO.DirectoryInfo(start);
    while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AgentBattle.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? start;
}

var solutionRoot = FindSolutionRoot(builder.Environment.ContentRootPath);
var battlesDir     = System.IO.Path.GetFullPath(builder.Configuration["Paths:BattlesDirectory"] ?? "battles", solutionRoot);
var agentsDir      = System.IO.Path.GetFullPath(builder.Configuration["Paths:AgentsDirectory"]  ?? "agents",  solutionRoot);
var suggestionsPath = System.IO.Path.GetFullPath(builder.Configuration["Paths:SuggestionsFile"] ?? "battles/suggestions.json", solutionRoot);

builder.Services.AddSingleton(_ => new BattleArchive(battlesDir));
builder.Services.AddSingleton(_ => new AgentRegistry(agentsDir));
builder.Services.AddSingleton(_ => new SuggestionStore(suggestionsPath));
builder.Services.AddSingleton<StatsAggregator>();
builder.Services.AddSingleton<StatsCache>();

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

public partial class Program;
