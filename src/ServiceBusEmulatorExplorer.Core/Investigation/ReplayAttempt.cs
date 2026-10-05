using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public enum ReplaySendStatus { Confirmed, Uncertain, NotSent }
public enum ReplaySendFailure { None, Cancelled, Timeout, Authorization, EntityUnavailable, ServiceBusy, QuotaExceeded, Transport, Unknown }

/// <summary>Durable replay identity, without message bodies or connection credentials.</summary>
public sealed record ReplayAttempt(Guid AttemptId, string ProfileId, string NamespaceFingerprint,
    ReplayReservation Reservation, EntityAddress OriginalSource, long OriginalSequenceNumber,
    string OriginalFingerprint, string OriginalMessageId, DateTimeOffset RequestedAtUtc,
    ReplaySendStatus SendStatus, DateTimeOffset? SentAtUtc = null)
{
    public ReplayObservation? Observation { get; init; }
    public ReplayOriginalStatus OriginalStatus { get; init; }
    public bool HiddenFromHistory { get; init; }
    public ReplaySendFailure SendFailure { get; init; }
    public static string? MessageIdProblem(string? messageId) =>
        string.IsNullOrWhiteSpace(messageId) ? "Enter a message ID."
        : messageId.Length > 128 ? "Use a message ID of at most 128 characters."
        : null;

    public void Validate()
    {
        if (AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(ProfileId)
            || !IsFingerprint(NamespaceFingerprint) || !IsFingerprint(OriginalFingerprint)
            || OriginalSequenceNumber < 0 || OriginalMessageId is null || RequestedAtUtc == default
            || !Enum.IsDefined(SendStatus) || !Enum.IsDefined(OriginalStatus) || !Enum.IsDefined(SendFailure) || Reservation is null
            || MessageIdProblem(Reservation.MessageId) is not null
            || (SendStatus == ReplaySendStatus.Confirmed && SentAtUtc is null))
            throw new ArgumentException("Invalid saved replay attempt.");
        ReplayLineage.ValidateFamily(Reservation.Family);
        _ = ReplayLineage.Destination(OriginalSource);
        Observation?.Validate();
    }

    private static bool IsFingerprint(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
