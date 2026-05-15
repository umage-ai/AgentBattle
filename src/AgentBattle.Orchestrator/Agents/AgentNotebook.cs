namespace AgentBattle.Orchestrator.Agents;

/// <summary>
/// Per-seat self-curated notebook. Each agent can append short notes via the
/// take_note tool; only their own notes are read back into their next prompt.
/// Bounded so a chatty agent can't blow up its own context.
/// </summary>
public sealed class AgentNotebook
{
    private const int MaxNotesPerSeat = 12;
    private const int MaxNoteLength = 240;

    private readonly Dictionary<int, List<NoteEntry>> _notes = new();

    public AgentNotebook(IEnumerable<int> seats)
    {
        foreach (var s in seats) _notes[s] = new List<NoteEntry>();
    }

    public void Add(int seat, int handNo, string text)
    {
        if (!_notes.TryGetValue(seat, out var list)) return;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return;
        if (trimmed.Length > MaxNoteLength) trimmed = trimmed.Substring(0, MaxNoteLength) + "…";
        list.Add(new NoteEntry(handNo, trimmed));
        while (list.Count > MaxNotesPerSeat) list.RemoveAt(0);
    }

    public IReadOnlyList<NoteEntry> For(int seat) =>
        _notes.TryGetValue(seat, out var list) ? list : Array.Empty<NoteEntry>();

    public sealed record NoteEntry(int HandNo, string Text);
}
