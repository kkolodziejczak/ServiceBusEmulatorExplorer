using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Messaging;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public enum EntityKind
{
    Queue,
    Topic,
    Subscription
}

public enum MessageBucket
{
    Active,
    DeadLetter
}

public sealed record EntityAddress(EntityKind Kind, string Name, string? TopicName = null);

public sealed record EntityRuntimeCounts(
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long ScheduledMessageCount,
    long TotalMessageCount);

public sealed record EntityMetadata(
    string Path,
    string Status,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    TimeSpan? LockDuration,
    int? MaxDeliveryCount,
    TimeSpan? DefaultMessageTimeToLive,
    bool? RequiresSession,
    bool? RequiresDuplicateDetection);

public sealed record ServiceBusEntityNode(
    EntityKind Kind,
    string Name,
    string? TopicName,
    EntityRuntimeCounts Counts,
    EntityMetadata Metadata);

public sealed record ExplorerMessage(
    string MessageId,
    long SequenceNumber,
    string Body,
    string BodyPreview,
    int BodySizeBytes,
    DateTimeOffset? EnqueuedTime,
    DateTimeOffset? ExpiresAt,
    int DeliveryCount,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? Subject,
    IReadOnlyDictionary<string, object?> ApplicationProperties,
    IReadOnlyDictionary<string, object?> SystemProperties)
{
    /// <summary>Original broker bytes, independent of the decoded body used for text display.</summary>
    public BinaryData? RawBody { get; init; }

    /// <summary>
    /// <c>false</c> when the AMQP body is a value or sequence; <see cref="Body"/> then only describes it and
    /// <see cref="RawBody"/> is <c>null</c>, so the message cannot be replayed without changing it.
    /// </summary>
    public bool HasDataBody { get; init; } = true;
    public Azure.Messaging.ServiceBus.ServiceBusMessageState? BrokerState { get; init; }
}

public sealed record SendMessageCommand(
    EntityAddress Destination,
    string Body,
    string? ContentType = null,
    string? CorrelationId = null,
    string? SessionId = null,
    string? Subject = null,
    IReadOnlyDictionary<string, object?>? ApplicationProperties = null);

public interface IServiceBusClientFactory : IAsyncDisposable
{
    /// <summary>
    /// Indicates whether the connected broker provides reliable runtime message counts.
    /// The local development emulator exposes count fields that may remain zero while
    /// messages are present, so callers must represent those counts as unavailable.
    /// </summary>
    bool SupportsRuntimeCounts => true;

    Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken);

    ServiceBusAdministrationClient AdministrationClient { get; }

    ServiceBusClient RuntimeClient { get; }
}

public interface IServiceBusMessageService
{
    Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
        EntityAddress address,
        MessageBucket bucket,
        int take,
        long? fromSequenceNumber,
        CancellationToken cancellationToken);

    Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken);
}
