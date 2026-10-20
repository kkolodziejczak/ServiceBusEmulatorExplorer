namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>A finished Workbench run as it is kept for the session and exported: each message's outcome, the scheduled receipts and the cancellation attempts.</summary>
public sealed record PrototypeRunSnapshot(string Profile, string Target, bool Scheduled,
    IReadOnlyList<(int Row, string MessageId, string Outcome, string Details)> Results,
    IReadOnlyList<(int Row, string Receipt, string Payload, string Outcome, string DueTime, bool Selected, string CancellationStatus, string AttemptTime)> ScheduledResults)
{
    public IReadOnlyList<PrototypeCancellationAttempt> CancellationAttempts { get; init; } = [];
}

/// <summary>One attempt to cancel a scheduled message, kept even when a later attempt succeeds.</summary>
public sealed record PrototypeCancellationAttempt(int Row, string Receipt, string Payload, string Outcome, DateTimeOffset RequestedAtUtc);
