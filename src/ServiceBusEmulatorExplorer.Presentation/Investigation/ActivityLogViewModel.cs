using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>The kind of an activity log line, shown by its label and colour.</summary>
public enum ActivityLevel
{
    Info,
    Watch,
    Warning
}

/// <summary>
/// The activity log: its lines with their time in UTC or local time, whether it is expanded, clearing it, and the
/// time zone used across the investigation.
/// </summary>
public sealed class ActivityLogViewModel
{
    private readonly InvestigationWorkspace workspace;

    public ActivityLogViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    public IReadOnlyList<ActivityLine> Lines => workspace.Activity.Select(entry => new ActivityLine(
        Time(entry.TimestampUtc), Date(entry.TimestampUtc),
        entry.Warning ? ActivityLevel.Warning : entry.Watch ? ActivityLevel.Watch : ActivityLevel.Info, entry.Message)).ToArray();

    public bool IsEmpty => workspace.Activity.Count == 0;

    /// <summary>The time of the latest line, or an empty text when the log is empty.</summary>
    public string LastTime => workspace.Activity.LastOrDefault() is { } last ? Time(last.TimestampUtc) : "";

    /// <summary>The full date of the latest line, or <c>null</c> when the log is empty.</summary>
    public string? LastDate => workspace.Activity.LastOrDefault() is { } last ? Date(last.TimestampUtc) : null;

    public bool IsExpanded => workspace.Preferences.LogExpanded;

    public string Heading => IsExpanded ? "Activity log · Expanded" : "Activity log · Collapsed";

    /// <summary>The expand button's tooltip and accessible name.</summary>
    public string ToggleLabel => IsExpanded ? "Collapse activity log" : "Expand activity log";

    public Task ToggleAsync() => workspace.UpdateDisplayPreferencesAsync(logExpanded: !IsExpanded);

    public void Clear() => workspace.Activity.Clear();

    /// <summary>Times show in UTC; otherwise in local time.</summary>
    public bool UsesUtc => workspace.Preferences.TimestampDisplay == TimestampDisplay.Utc;

    public Task SetUsesUtcAsync(bool utc) =>
        workspace.UpdateDisplayPreferencesAsync(timestampDisplay: utc ? TimestampDisplay.Utc : TimestampDisplay.Local);

    /// <summary>The message list's Enqueued column header, which names the time zone.</summary>
    public string EnqueuedHeader => UsesUtc ? "Enqueued (UTC)" : "Enqueued (Local)";

    private string Date(DateTimeOffset instant)
    {
        bool local = !UsesUtc;
        return DateDisplay.Timestamp(local ? instant.ToLocalTime() : instant.ToUniversalTime(), workspace.Preferences.DateFormat)
            + (local ? " Local" : " UTC");
    }

    private string Time(DateTimeOffset instant) => UsesUtc
        ? instant.ToString("HH:mm:ss") + " UTC"
        : instant.ToLocalTime().ToString("HH:mm:ss") + " Local";
}

/// <summary>One activity log line: its time, its full date for the tooltip, its kind, and the message.</summary>
public sealed record ActivityLine(string Time, string Date, ActivityLevel Level, string Message)
{
    /// <summary>The fixed-width label before the message.</summary>
    public string Label => Level switch
    {
        ActivityLevel.Warning => "WARN    ",
        ActivityLevel.Watch => "WATCH   ",
        _ => "INFO    "
    };
}
