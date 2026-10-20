using System.Collections.ObjectModel;
using System.Globalization;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>
/// A Message Workbench review run: the prepared messages sent, or scheduled for one instant, one at a time, what
/// happened to each, the receipts of scheduled messages, and the attempts to cancel them. The prototype simulates the
/// broker, so every outcome is a sample.
/// </summary>
public sealed class WorkbenchRun(Func<DateTimeOffset> utcNow)
{
    /// <summary>Attempts allowed per receipt; after that the receipt can no longer be cancelled from the Workbench.</summary>
    public const int MaximumCancellationAttempts = 10;

    private IReadOnlyList<string> messageIds = [];
    private DateTimeOffset? dueUtc;
    private string target = "";

    public ObservableCollection<WorkbenchSendResult> Results { get; } = [];
    public ObservableCollection<WorkbenchScheduledReceipt> ScheduledReceipts { get; } = [];
    public ObservableCollection<PrototypeCancellationAttempt> CancellationAttempts { get; } = [];
    public bool Scheduled { get; private set; }

    /// <summary>How many messages of the current run have been acknowledged.</summary>
    public int Sent { get; private set; }

    /// <summary>
    /// Starts sending the prepared messages with <paramref name="preparedIds"/> to <paramref name="targetName"/>, or
    /// scheduling them for <paramref name="scheduledFor"/>, and clears the previous run's results.
    /// </summary>
    public void Start(IReadOnlyList<string> preparedIds, DateTimeOffset? scheduledFor, string targetName)
    {
        ArgumentNullException.ThrowIfNull(preparedIds);
        messageIds = preparedIds;
        dueUtc = scheduledFor;
        Scheduled = scheduledFor is not null;
        target = targetName;
        Sent = 0;
        Results.Clear();
        ScheduledReceipts.Clear();
        CancellationAttempts.Clear();
    }

    /// <summary>Sends or schedules the next message and returns whether every message has now been acknowledged.</summary>
    public bool SendNext(DateDisplayFormat dateFormat)
    {
        Sent++;
        string outcome = Scheduled ? "Confirmed scheduled" : "Confirmed sent";
        Results.Add(new(Sent, messageIds[Sent - 1], outcome, Scheduled ? "Sample receipt retained" : "Sample acknowledgement"));
        if (Scheduled)
            ScheduledReceipts.Add(new(Sent, (700 + Sent).ToString(CultureInfo.InvariantCulture), $"{target}-{Sent}.json", outcome,
                dueUtc!.Value.ToString("O", CultureInfo.InvariantCulture)) { Selected = true, DateFormat = dateFormat });
        return Sent == messageIds.Count;
    }

    /// <summary>Stops sending: the message in flight has an unknown outcome and the ones after it were not attempted.</summary>
    public void Stop()
    {
        if (Sent >= messageIds.Count) return;
        int unknown = Sent + 1;
        Results.Add(new(unknown, messageIds[unknown - 1], "Outcome unknown", "In flight when stopped"));
        for (int row = unknown + 1; row <= messageIds.Count; row++)
            Results.Add(new(row, messageIds[row - 1], "Not attempted", "Not submitted"));
    }

    /// <summary>The run as it is kept and exported, or <c>null</c> before any message has an outcome.</summary>
    public PrototypeRunSnapshot? Capture(string profile, string targetName) => Results.Count == 0 ? null : new PrototypeRunSnapshot(
        profile, targetName, Scheduled,
        Results.Select(row => (row.Row, row.MessageId, row.Outcome, row.Details)).ToArray(),
        ScheduledReceipts.Select(row => (row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime,
            row.Selected, row.CancellationStatus, row.AttemptTime)).ToArray())
        { CancellationAttempts = CancellationAttempts.ToArray() };

    /// <summary>Shows a kept run again, with its receipts' selection, cancellation status and attempts.</summary>
    public void Restore(PrototypeRunSnapshot run, DateDisplayFormat dateFormat)
    {
        ArgumentNullException.ThrowIfNull(run);
        Scheduled = run.Scheduled;
        Results.Clear();
        foreach (var row in run.Results)
            Results.Add(new(row.Row, row.MessageId, row.Outcome, row.Details));
        ScheduledReceipts.Clear();
        foreach (var row in run.ScheduledResults)
            ScheduledReceipts.Add(new(row.Row, row.Receipt, row.Payload, row.Outcome, row.DueTime)
            { Selected = row.Selected, CancellationStatus = row.CancellationStatus, AttemptTime = row.AttemptTime, DateFormat = dateFormat });
        CancellationAttempts.Clear();
        foreach (var attempt in run.CancellationAttempts) CancellationAttempts.Add(attempt);
    }

    /// <summary>Shows every receipt's due time in <paramref name="dateFormat"/>.</summary>
    public void UseDateFormat(DateDisplayFormat dateFormat)
    {
        foreach (var row in ScheduledReceipts) row.DateFormat = dateFormat;
    }

    /// <summary>The distinct due times of the scheduled receipts, for the cancellation header.</summary>
    public string DueTimes => string.Join("; ", ScheduledReceipts.Select(row => row.DueTimeDisplay).Distinct());

    /// <summary>Whether any selected receipt can still be cancelled.</summary>
    public bool CanRetryCancellation => ScheduledReceipts.Any(row => row.Selected && CanCancel(row));

    /// <summary>Explains what cancelling leaves unchanged, or that a receipt has used all its attempts.</summary>
    public string CancellationNotice => ScheduledReceipts.Any(row => AttemptCount(row) >= MaximumCancellationAttempts)
        ? "10 attempts reached for a receipt; further attempts for that receipt are disabled. Dispatch results remain unchanged."
        : "Dispatch results remain unchanged. Retry requires fresh confirmation; only future, unacknowledged receipts are eligible.";

    /// <summary>The selected receipts that can be cancelled now.</summary>
    public IReadOnlyList<WorkbenchScheduledReceipt> CancellableSelection() =>
        ScheduledReceipts.Where(row => row.Selected && CanCancel(row)).ToArray();

    /// <summary>
    /// Attempts to cancel <paramref name="receipts"/> in order, skipping any that can no longer be cancelled. Once an
    /// attempt's outcome is unknown, the remaining receipts are recorded as not attempted.
    /// </summary>
    public void Cancel(IEnumerable<WorkbenchScheduledReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        bool stopped = false;
        foreach (var row in receipts)
        {
            if (!CanCancel(row)) continue;
            row.CancellationStatus = stopped ? "Not attempted" :
                row.Row % 2 == 0 ? "Outcome unknown" : "Cancellation acknowledged";
            DateTimeOffset requestedAt = utcNow();
            row.AttemptTime = requestedAt.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture);
            CancellationAttempts.Add(new(row.Row, row.Receipt, row.Payload, row.CancellationStatus, requestedAt));
            if (row.CancellationStatus == "Cancellation acknowledged") row.Selected = false;
            else if (row.CancellationStatus == "Outcome unknown") stopped = true;
        }
    }

    private bool CanCancel(WorkbenchScheduledReceipt row) =>
        row.Outcome is "Confirmed scheduled" or "Scheduled" &&
        row.CancellationStatus != "Cancellation acknowledged" &&
        AttemptCount(row) < MaximumCancellationAttempts &&
        long.TryParse(row.Receipt, NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
        row.DueAtUtc is { } due && due.UtcDateTime > utcNow().UtcDateTime;

    private int AttemptCount(WorkbenchScheduledReceipt row) =>
        CancellationAttempts.Count(attempt => attempt.Row == row.Row && attempt.Receipt == row.Receipt);
}

/// <summary>What happened to one message of a run.</summary>
public sealed record WorkbenchSendResult(int Row, string MessageId, string Outcome, string Details);

/// <summary>The receipt of a scheduled message, which the user may select for cancellation.</summary>
public sealed class WorkbenchScheduledReceipt(int row, string receipt, string payload, string outcome, string dueTime)
{
    public int Row { get; } = row;
    public string Receipt { get; } = receipt;
    public string Payload { get; } = payload;
    public string Outcome { get; } = outcome;
    // New runs capture ISO UTC; older session fixtures are parsed once at this boundary.
    public string DueTime { get; } = dueTime;
    public DateTimeOffset? DueAtUtc { get; } = ReadDueInstant(dueTime);
    public DateDisplayFormat DateFormat { get; set; }
    public string DueTimeDisplay => DueAtUtc is { } instant
        ? DateDisplay.Timestamp(instant, DateFormat, seconds: false) + " UTC" : DueTime;
    public bool Selected { get; set; }
    public string CancellationStatus { get; set; } = "Eligible";
    public string AttemptTime { get; set; } = "—";

    private static DateTimeOffset? ReadDueInstant(string text)
    {
        string value = text.Split('·')[0].Replace("UTC", "", StringComparison.Ordinal).Trim();
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var instant)
            || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, styles, out instant) ? instant : null;
    }
}
