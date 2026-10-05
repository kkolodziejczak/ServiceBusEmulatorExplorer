using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record ReplayHistoryRow(ReplayAttempt Attempt, WorkspacePreferences Preferences, bool CanRemove = false,
    bool BrokerAvailable = true, bool CanDeleteOriginal = false)
{
    public string OriginalMessageId => Attempt.OriginalMessageId;
    public string DisplayOriginalId => CompactId(OriginalMessageId);
    public string DisplayReplayId => CompactId(MessageId);
    public string OriginalSearchName => "Find original " + OriginalMessageId;
    public string ReplaySearchName => "Find replay " + MessageId;
    public string RemoveName => "Remove from history " + MessageId;
    public string Destination => FormatAddress(ReplayLineage.Destination(Attempt.OriginalSource));
    public string OriginalSourceName => Attempt.OriginalSource.Name;
    public ReplayHistorySource SourceLocation => new(Attempt.OriginalSource);
    public string ObservationTime => Attempt.Observation is { } observation
        ? "Checked " + Format(observation.CheckedAtUtc)
        : "";
    private static string CompactId(string id) => id.Length <= 24 ? id : id[..12] + "\u2026" + id[^8..];
    private static string FormatAddress(EntityAddress source) => source.TopicName is null
        ? source.Name + " (" + source.Kind + ")" : source.TopicName + " / " + source.Name;
    public string MessageId => Attempt.Reservation.MessageId;
    public string Sent => (Attempt.SendStatus == ReplaySendStatus.Confirmed ? "Sent " : "Requested ")
        + Format(Attempt.SentAtUtc ?? Attempt.RequestedAtUtc) + " · "
        + (Attempt.SendStatus == ReplaySendStatus.Confirmed ? "Accepted" : SendSummary);
    private string SendSummary => Attempt.SendStatus switch
    {
        ReplaySendStatus.Confirmed => "Accepted by broker",
        ReplaySendStatus.Uncertain => "Send uncertain",
        _ => "Not sent"
    };
    public bool HasDeadLetterCopy => Attempt.Observation?.Locations.Any(location => location.Bucket == MessageBucket.DeadLetter) == true;
    public IReadOnlyList<ReplayHistoryState> ObservedStates
    {
        get
        {
            if (Attempt.Observation is not { } observation) return [new("Not checked")];
            if (observation.Locations.Count == 0)
                return [new(observation.IsAbsent ? "Not found" : "Incomplete / unavailable", IsProblem: !observation.IsAbsent)];
            var states = observation.Locations
                .GroupBy(location => location.Bucket == MessageBucket.DeadLetter ? "DLQ" : location.State)
                .OrderByDescending(group => group.Key == "DLQ")
                .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new ReplayHistoryState($"{group.Count()} {group.Key}", group.Key == "DLQ"));
            return observation.IsComplete ? states.ToArray()
                : states.Append(new ReplayHistoryState("Partial coverage", IsProblem: true)).ToArray();
        }
    }
    public string Observation => string.Join(" \u00b7 ", ObservedStates.Select(state => state.Label));
    public string ObservationTimeTooltip => Attempt.Observation is not { } observation ? ""
        : $"{observation.SourceCount} receiving sources; {observation.ScannedDeliveries} deliveries scanned. Active and DLQ. "
            + (observation.Limitation ?? "Pending topic schedules and forwarding outside this namespace are not covered.");
    public string ObservationReason
    {
        get
        {
            string diagnostic = ReplaySendDiagnostics.Describe(Attempt.SendFailure);
            if (diagnostic.Length > 0) return diagnostic;
            if (Attempt.Observation is not { } observation) return "";
            return !observation.IsComplete ? "Scan incomplete. " + (observation.Limitation ?? "Absence has not been established.")
                : "";
        }
    }
    public bool HasObservationReason => ObservationReason.Length > 0;
    public string Original => Attempt.OriginalStatus switch
    {
        ReplayOriginalStatus.Deleted => "Deleted",
        ReplayOriginalStatus.Uncertain => "Delete uncertain",
        ReplayOriginalStatus.Unavailable => "Unavailable",
        _ => "Retained"
    };
    public string Detail
    {
        get
        {
            string diagnostic = ReplaySendDiagnostics.Describe(Attempt.SendFailure);
            string send = SendSummary + ". " + (diagnostic.Length == 0 ? "" : diagnostic + "\n");
            if (Attempt.Observation is not { } observation) return send + "Active/DLQ status has not been checked. Check before manually retrying.";
            string result = observation.IsAbsent ? "Not found in active messages or DLQ during the last complete scan. API processing is not verified."
                : observation.Locations.Count > 0 ? "Last observed in: " + string.Join("; ", observation.Locations.Select(location =>
                    (location.Source.TopicName is null ? location.Source.Name : location.Source.TopicName + " / " + location.Source.Name)
                    + " \u00b7 " + location.State)) + "."
                : "The scan was incomplete; absence has not been established.";
            return send + result + "\nLast checked: " + Format(observation.CheckedAtUtc)
                + $" \u00b7 {observation.SourceCount} receiving sources \u00b7 Active and DLQ \u00b7 {observation.ScannedDeliveries} deliveries scanned."
                + (observation.Limitation is null ? "" : "\n" + observation.Limitation)
                + "\nLast observation only; pending topic schedules and forwarding outside this namespace are not covered.";
        }
    }
    private string Format(DateTimeOffset instant, bool multiline = false)
    {
        var value = Preferences.TimestampDisplay == TimestampDisplay.Utc ? instant.ToUniversalTime() : instant.ToLocalTime();
        return DateDisplay.Date(value.DateTime, Preferences.DateFormat) + (multiline ? "\n" : " ")
            + value.ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture)
            + (Preferences.TimestampDisplay == TimestampDisplay.Utc ? " UTC" : " Local");
    }
}

public sealed record ReplayHistoryState(string Label, bool IsDeadLetter = false, bool IsProblem = false)
{
    public bool IsActive => Label.EndsWith(" Active", StringComparison.Ordinal);
    public bool IsUnchecked => Label == "Not checked";
}

public sealed record ReplayHistorySource(EntityAddress Address)
{
    public string SourceTopic => Address.TopicName ?? string.Empty;
    public string SourceName => Address.Name;
    public bool IsSubscription => Address.Kind == EntityKind.Subscription;
    public string SourceDetail => IsSubscription
        ? $"Topic: {SourceTopic}\nSubscription: {SourceName}"
        : $"{Address.Kind}: {SourceName}";
}
