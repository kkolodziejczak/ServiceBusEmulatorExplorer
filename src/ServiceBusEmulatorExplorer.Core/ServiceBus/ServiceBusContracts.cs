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
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    TimeSpan? LockDuration,
    int? MaxDeliveryCount);

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
    DateTimeOffset? EnqueuedTime,
    DateTimeOffset? ExpiresAt,
    int DeliveryCount,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
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

public sealed record CreateQueueCommand(string Name);

public sealed record UpdateQueueCommand(string Name);

public sealed record CreateTopicCommand(string Name);

public sealed record UpdateTopicCommand(string Name);

public sealed record CreateSubscriptionCommand(string TopicName, string SubscriptionName);

public sealed record UpdateSubscriptionCommand(string TopicName, string SubscriptionName);

public sealed record SendMessageCommand(EntityAddress Destination, string Body);

public interface IServiceBusClientFactory : IAsyncDisposable
{
    Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken);

    ServiceBusAdministrationClient AdministrationClient { get; }

    ServiceBusClient RuntimeClient { get; }
}

public interface IServiceBusAdministrationService
{
    Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken);

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

    Task DeleteMessagesAsync(
        EntityAddress address,
        MessageBucket bucket,
        IReadOnlyList<long> sequenceNumbers,
        CancellationToken cancellationToken);
}

public interface IDeadLetterReplayService
{
    Task<ReplayResult> ReplayAsync(ReplayRequest request, CancellationToken cancellationToken);
}
