namespace ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

/// <summary>
/// Find in the inspected text: every case-insensitive match of the query, which match is selected, moving to the next
/// or previous one with wrap-around, and the count shown beside the find box.
/// </summary>
public sealed class TextFindViewModel
{
    private readonly List<int> matches = [];
    private string? text;
    private string query = string.Empty;

    /// <summary>Where each match starts, in order.</summary>
    public IReadOnlyList<int> Matches => matches;

    /// <summary>The selected match, or -1 when none is selected.</summary>
    public int Current { get; private set; } = -1;

    public int QueryLength => query.Length;

    /// <summary>"3 of 5" for a selected match, "5 matches" when none is selected, "No matches", or nothing without a query.</summary>
    public string CountText => matches.Count == 0 ? query.Length == 0 ? "" : "No matches"
        : Current < 0 ? $"{matches.Count} matches" : $"{Current + 1} of {matches.Count}";

    /// <summary>
    /// Finds the matches of <paramref name="newQuery"/> in <paramref name="newText"/> and selects the first. Returns
    /// <c>false</c> without changing anything when neither changed, unless <paramref name="force"/> is set.
    /// </summary>
    public bool Search(string newText, string newQuery, bool force)
    {
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(newQuery);
        if (!force && newText == text && newQuery == query) return false;
        text = newText;
        query = newQuery;
        matches.Clear();
        if (query.Length > 0)
        {
            int offset = 0;
            while (offset <= text.Length - query.Length)
            {
                int match = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                matches.Add(match);
                offset = match + query.Length;
            }
        }
        Current = matches.Count == 0 ? -1 : 0;
        return true;
    }

    /// <summary>Keeps the matches but selects none, so the user's own selection in the editor stays.</summary>
    public void Unselect() => Current = -1;

    /// <summary>Selects the next match (or the previous for a negative direction), wrapping around; <c>false</c> without matches.</summary>
    public bool Move(int direction)
    {
        if (matches.Count == 0) return false;
        Current = Current < 0 ? (direction > 0 ? 0 : matches.Count - 1)
            : (Current + direction + matches.Count) % matches.Count;
        return true;
    }

    /// <summary>Forgets the searched text so the next search runs again; used when find closes.</summary>
    public void Forget() => text = null;
}
