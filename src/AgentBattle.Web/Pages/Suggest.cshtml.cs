using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AgentBattle.Web.Services;

namespace AgentBattle.Web.Pages;

public class SuggestModel(SuggestionStore store) : PageModel
{
    public enum StatusKind { None, Ok, Error }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<BattleSuggestion> Suggestions { get; private set; } = [];
    public StatusKind Status { get; private set; } = StatusKind.None;
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        Suggestions = store.List();
        if (TempData["suggest_ok"] != null) Status = StatusKind.Ok;
    }

    public IActionResult OnPost()
    {
        var agents = (Input.Agents ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToArray();

        if (agents.Length < 2)
        {
            Status = StatusKind.Error;
            ErrorMessage = "Please name at least two agents you'd like to see battle.";
            Suggestions = store.List();
            return Page();
        }

        store.Add(
            suggestedBy: Input.SuggestedBy ?? "",
            game: string.IsNullOrWhiteSpace(Input.Game) ? "poker-6max" : Input.Game,
            agents: agents,
            note: Input.Note ?? "");

        // PRG so a refresh doesn't resubmit.
        TempData["suggest_ok"] = "1";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        public string? SuggestedBy { get; set; }
        public string? Game { get; set; } = "poker-6max";
        public string[]? Agents { get; set; }
        public string? Note { get; set; }
    }
}
