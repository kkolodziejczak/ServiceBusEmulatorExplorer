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
    IReadOnlyDictionary<string, object?> SystemProperties);

public sealed record ReplayRequest(
    EntityAddress Source,
    EntityAddress Destination,
    long SequenceNumber,
    string? EditedBody,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    IReadOnlyDictionary<string, object?> ApplicationProperties,
    ReplayIdPolicy IdPolicy,
    bool DeleteOriginal);

public sealed record ReplayResult(string NewMessageId, bool OriginalDeleted);

public sealed record CreateQueueCommand(
    string Name,
    TimeSpan? LockDuration = null,
    int? MaxDeliveryCount = null,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool RequiresSession = false,
    bool RequiresDuplicateDetection = false);

public sealed record UpdateQueueCommand(
    string Name,
    TimeSpan? LockDuration = null,
    int? MaxDeliveryCount = null,
    TimeSpan? DefaultMessageTimeToLive = null);

public sealed record CreateTopicCommand(
    string Name,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool RequiresDuplicateDetection = false);

public sealed record UpdateTopicCommand(
    string Name,
    TimeSpan? DefaultMessageTimeToLive = null);

public sealed record CreateSubscriptionCommand(
    string TopicName,
    string SubscriptionName,
    TimeSpan? LockDuration = null,
    int? MaxDeliveryCount = null,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool RequiresSession = false);

public sealed record UpdateSubscriptionCommand(
    string TopicName,
    string SubscriptionName,
    TimeSpan? LockDuration = null,
    int? MaxDeliveryCount = null,
    TimeSpan? DefaultMessageTimeToLive = null);

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
    Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken);

    ServiceBusAdministrationClient AdministrationClient { get; }

    ServiceBusClient RuntimeClient { get; }
}

public interface IServiceBusEntityBrowser
{
    Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken);
}

public interface IServiceBusAdministrationService : IServiceBusEntityBrowser
{
    Task CreateQueueAsync(CreateQueueCommand command, CancellationToken cancellationToken);

    Task UpdateQueueAsync(UpdateQueueCommand command, CancellationToken cancellationToken);

    Task DeleteQueueAsync(string name, CancellationToken cancellationToken);

    Task CreateTopicAsync(CreateTopicCommand command, CancellationToken cancellationToken);

    Task UpdateTopicAsync(UpdateTopicCommand command, CancellationToken cancellationToken);

    Task DeleteTopicAsync(string name, CancellationToken cancellationToken);

    Task CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken);

    Task UpdateSubscriptionAsync(UpdateSubscriptionCommand command, CancellationToken cancellationToken);

    Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken);
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

public interface IDeadLetterReplayService
{
    Task<ReplayResult> ReplayAsync(ReplayRequest request, CancellationToken cancellationToken);
}
