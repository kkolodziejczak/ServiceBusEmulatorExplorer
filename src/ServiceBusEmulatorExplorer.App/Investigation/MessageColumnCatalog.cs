using System.Globalization;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>Stable column identities and typed message values, independent of the grid.</summary>
public static class MessageColumnCatalog
{
    public const string Event = "default:event";
    public const string Correlation = "default:correlation";
    public const string Enqueued = "default:enqueued";
    public const string ApplicationPrefix = "application:";
    public static IReadOnlyList<string> Defaults { get; } = [Event, Correlation, Enqueued];
    public static IReadOnlyList<MessageColumnDefinition> Standard { get; } =
    [
        new(Event, "Event / Message ID", "Default columns"),
        new(Correlation, "Correlation ID", "Default columns"),
        new(Enqueued, "Enqueued", "Default columns"),
        new("system:MessageId", "Message ID", "System properties"),
        new("system:Subject", "Subject", "System properties"),
        new("system:SessionId", "Session ID", "System properties"),
        new("system:DeliveryCount", "Delivery count", "System properties"),
        new("system:SequenceNumber", "Sequence number", "System properties"),
        new("system:ContentType", "Content type", "System properties"),
        new("system:ExpiresAtUtc", "Expires at", "System properties"),
        new("system:ScheduledEnqueueTimeUtc", "Scheduled enqueue time", "System properties"),
        new("system:TimeToLive", "Time to live", "System properties"),
        new("system:PartitionKey", "Partition key", "System properties"),
        new("system:TransactionPartitionKey", "Transaction partition key", "System properties"),
        new("system:ReplyTo", "Reply to", "System properties"),
        new("system:ReplyToSessionId", "Reply-to session ID", "System properties"),
        new("system:To", "To", "System properties"),
        new("system:DeadLetterReason", "Dead-letter reason", "System properties"),
        new("system:DeadLetterErrorDescription", "Dead-letter description", "System properties"),
        new("system:BodySize", "Body size (bytes)", "System properties")
    ];

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? selection)
    {
        if (selection is null) return Defaults.ToArray();
        var known = Standard.Select(column => column.Id).ToHashSet(StringComparer.Ordinal);
        string[] valid = selection.Where(id => id is not null && (known.Contains(id) ||
            id.StartsWith(ApplicationPrefix, StringComparison.Ordinal) && id.Length > ApplicationPrefix.Length))
            .Distinct(StringComparer.Ordinal).ToArray();
        return valid.Length == 0 ? Defaults.ToArray() : valid;
    }

    public static IReadOnlyList<string> NormalizeOrder(IReadOnlyList<string>? order, IReadOnlyList<string>? selected) =>
        Normalize(order).Concat(Normalize(selected)).Distinct(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> ReorderVisible(IReadOnlyList<string> order, IReadOnlyList<string> visible)
    {
        var visibleIds = visible.ToHashSet(StringComparer.Ordinal);
        var remaining = new Queue<string>(visible);
        return order.Select(id => visibleIds.Contains(id) ? remaining.Dequeue() : id).ToArray();
    }

    public static IReadOnlyList<MessageColumnDefinition> Discover(IEnumerable<MessageRow> rows, IReadOnlyList<string> selected)
    {
        var keys = rows.SelectMany(row => row.Delivery.Message.ApplicationProperties.Keys)
            .Concat(selected.Where(id => id.StartsWith(ApplicationPrefix, StringComparison.Ordinal))
                .Select(id => id[ApplicationPrefix.Length..]))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Standard.Concat(keys.Select(key => new MessageColumnDefinition(
            ApplicationPrefix + key, key, "Application properties"))).ToArray();
    }

    public static object? Value(ExplorerMessage message, string id) => id switch
    {
        Event or "system:Subject" => message.Subject,
        Correlation => message.CorrelationId,
        Enqueued => message.EnqueuedTime,
        "system:MessageId" => message.MessageId,
        "system:SessionId" => message.SessionId,
        "system:DeliveryCount" => message.DeliveryCount,
        "system:SequenceNumber" => message.SequenceNumber,
        "system:ContentType" => message.ContentType,
        "system:ExpiresAtUtc" => message.ExpiresAt,
        "system:BodySize" => message.BodySizeBytes,
        _ when id.StartsWith(ApplicationPrefix, StringComparison.Ordinal) =>
            message.ApplicationProperties.GetValueOrDefault(id[ApplicationPrefix.Length..]),
        _ when id.StartsWith("system:", StringComparison.Ordinal) => message.SystemProperties.GetValueOrDefault(id[7..]),
        _ => null
    };

    public static string Format(object? value, WorkspacePreferences preferences) => value switch
    {
        null => "—",
        DateTimeOffset instant => DateDisplay.Timestamp(preferences.TimestampDisplay == TimestampDisplay.Local
            ? instant.ToLocalTime() : instant.ToUniversalTime(), preferences.DateFormat) +
            (preferences.TimestampDisplay == TimestampDisplay.Local ? " Local" : " UTC"),
        DateTime instant => Format(new DateTimeOffset(instant.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(instant, DateTimeKind.Utc) : instant), preferences),
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    public static int Compare(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        if (left.GetType() == right.GetType() && left is IComparable sameType) return sameType.CompareTo(right);
        if (IsNumber(left) && IsNumber(right))
        {
            if (left is float or double || right is float or double)
                return Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
        }
        int typeOrder = string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
        return typeOrder != 0 ? typeOrder : string.CompareOrdinal(left.ToString(), right.ToString());
    }

    private static bool IsNumber(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

public sealed record MessageColumnDefinition(string Id, string Label, string Section);
