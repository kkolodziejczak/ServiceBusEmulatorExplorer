using System.Security.Cryptography;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public sealed record ReplayFamilyState(Guid FamilyId, string RootFingerprint, string OriginalMessageId,
    EntityAddress OriginalSource, long LastAttempt);
public sealed record ReplayReservation(ReplayFamilyState Family, string MessageId);

/// <summary>Explicit replay lineage and locally persisted attempt numbering; message IDs are not family keys.</summary>
public static class ReplayLineage
{
    public const string FamilyProperty = "sbe-explorer.replay.family";
    public const string RootProperty = "sbe-explorer.replay.root";
    public const string OriginalIdProperty = "sbe-explorer.replay.original-id";
    public const string SourceProperty = "sbe-explorer.replay.source";
    public const string AttemptProperty = "sbe-explorer.replay.attempt";
    private static readonly string[] LineageProperties = [FamilyProperty, RootProperty, OriginalIdProperty, SourceProperty, AttemptProperty];

    public static ReplayReservation Reserve(MessageDelivery delivery, IReadOnlyList<ReplayFamilyState> families)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(families);
        if (delivery.Identity.Bucket != MessageBucket.DeadLetter) throw new ArgumentException("Only DLQ deliveries can be replayed.");
        _ = Destination(delivery.Identity.Source);
        ReplayFamilyState? lineage = ReadLineage(delivery.Message.ApplicationProperties);
        string root = lineage?.RootFingerprint ?? Fingerprint(delivery);
        var existing = families.SingleOrDefault(family => string.Equals(family.RootFingerprint, root, StringComparison.OrdinalIgnoreCase));
        var existingId = lineage is null ? null : families.SingleOrDefault(family => family.FamilyId == lineage.FamilyId);
        if (existingId is not null && existingId != existing)
            throw new ArgumentException("Replay family identity conflicts with its saved root.");
        if (existing is not null) ValidateFamily(existing);
        if (existing is not null && lineage is not null && (existing.FamilyId != lineage.FamilyId
            || existing.OriginalSource != lineage.OriginalSource || existing.OriginalMessageId != lineage.OriginalMessageId))
            throw new ArgumentException("Replay lineage conflicts with the saved message family.");
        var family = existing ?? lineage ?? new ReplayFamilyState(new Guid(Convert.FromHexString(root)[..16]), root, delivery.Message.MessageId, delivery.Identity.Source, 0);
        long attempt = checked(Math.Max(family.LastAttempt, lineage?.LastAttempt ?? 0) + 1);
        family = family with { LastAttempt = attempt, RootFingerprint = root.ToUpperInvariant() };
        ValidateFamily(family);
        string suffix = $"-replay-{attempt}-{Guid.NewGuid():N}";
        string prefix = family.OriginalMessageId[..Math.Min(family.OriginalMessageId.Length, 128 - suffix.Length)];
        if (prefix.Length > 0 && char.IsHighSurrogate(prefix[^1])) prefix = prefix[..^1];
        return new(family, prefix + suffix);
    }

    private static ReplayFamilyState? ReadLineage(IReadOnlyDictionary<string, object?> properties)
    {
        if (!LineageProperties.Any(properties.ContainsKey)) return null;
        if (!LineageProperties.All(properties.ContainsKey)
            || properties[FamilyProperty] is not string familyId || !Guid.TryParseExact(familyId, "N", out var id)
            || properties[RootProperty] is not string root || properties[OriginalIdProperty] is not string originalId
            || properties[SourceProperty] is not string sourceJson || properties[AttemptProperty] is not long attempt)
            throw new ArgumentException("The message contains incomplete or invalid replay lineage.");
        EntityAddress source;
        try { source = JsonSerializer.Deserialize<EntityAddress>(sourceJson) ?? throw new JsonException(); }
        catch (JsonException exception) { throw new ArgumentException("The replay source lineage is invalid.", exception); }
        var family = new ReplayFamilyState(id, root.ToUpperInvariant(), originalId, source, attempt);
        ValidateFamily(family);
        return family;
    }

    public static void ValidateFamily(ReplayFamilyState family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.FamilyId == Guid.Empty || family.RootFingerprint is not { Length: 64 }
            || family.RootFingerprint.Any(character => !Uri.IsHexDigit(character))
            || family.OriginalMessageId is null || family.LastAttempt < 1)
            throw new ArgumentException("Invalid saved replay family.");
        _ = Destination(family.OriginalSource);
    }

    public static EntityAddress Destination(EntityAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.Name)) throw new ArgumentException("Replay source name is required.");
        return source.Kind switch
        {
            EntityKind.Queue => new(EntityKind.Queue, source.Name),
            EntityKind.Subscription when !string.IsNullOrWhiteSpace(source.TopicName) => new(EntityKind.Topic, source.TopicName),
            _ => throw new ArgumentException("Replay requires a queue or subscription DLQ source.")
        };
    }

    public static string Fingerprint(MessageDelivery original)
    {
        byte[] body = original.Message.RawBody?.ToArray() ?? BinaryData.FromString(original.Message.Body).ToArray();
        byte[] identity = JsonSerializer.SerializeToUtf8Bytes(new
        {
            original.Identity.Source,
            original.Identity.SequenceNumber,
            Enqueued = original.Message.EnqueuedTime?.UtcTicks,
            original.Message.MessageId,
            BodyHash = Convert.ToHexString(SHA256.HashData(body))
        });
        return Convert.ToHexString(SHA256.HashData(identity));
    }

    public static ServiceBusMessage CreateMessage(MessageDelivery delivery, ReplayReservation reservation, string? editedBody = null)
    {
        ValidateFamily(reservation.Family);
        if (delivery.Identity.Bucket != MessageBucket.DeadLetter) throw new ArgumentException("Only DLQ deliveries can be replayed.");
        var original = delivery.Message;
        var message = new ServiceBusMessage(editedBody is null ? original.RawBody ?? BinaryData.FromString(original.Body) : BinaryData.FromString(editedBody))
        {
            MessageId = reservation.MessageId, ContentType = original.ContentType, CorrelationId = original.CorrelationId,
            SessionId = original.SessionId, Subject = original.Subject,
            PartitionKey = TextProperty(original, "PartitionKey"), ReplyTo = TextProperty(original, "ReplyTo"),
            ReplyToSessionId = TextProperty(original, "ReplyToSessionId"), To = TextProperty(original, "To"),
            TransactionPartitionKey = TextProperty(original, "TransactionPartitionKey")
        };
        if (original.SystemProperties.TryGetValue("TimeToLive", out var ttl) && ttl is TimeSpan duration) message.TimeToLive = duration;
        foreach (var property in original.ApplicationProperties) message.ApplicationProperties[property.Key] = property.Value;
        message.ApplicationProperties[FamilyProperty] = reservation.Family.FamilyId.ToString("N");
        message.ApplicationProperties[RootProperty] = reservation.Family.RootFingerprint;
        message.ApplicationProperties[OriginalIdProperty] = reservation.Family.OriginalMessageId;
        message.ApplicationProperties[SourceProperty] = JsonSerializer.Serialize(reservation.Family.OriginalSource);
        message.ApplicationProperties[AttemptProperty] = reservation.Family.LastAttempt;
        return message;
    }

    private static string? TextProperty(ExplorerMessage message, string key) => message.SystemProperties.GetValueOrDefault(key) as string;
}
