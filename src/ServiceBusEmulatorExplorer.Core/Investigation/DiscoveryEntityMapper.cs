using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

internal static class DiscoveryEntityMapper
{
    public static DiscoveredEntity Queue(QueueProperties properties, QueueRuntimeProperties? runtime)
    {
        return Create(
            EntityKind.Queue,
            properties.Name,
            null,
            properties.Status.ToString(),
            runtime?.CreatedAt,
            runtime?.UpdatedAt,
            properties.LockDuration,
            properties.MaxDeliveryCount,
            properties.DefaultMessageTimeToLive,
            properties.RequiresSession,
            properties.RequiresDuplicateDetection);
    }

    public static DiscoveredEntity Topic(
        TopicProperties properties,
        TopicRuntimeProperties? runtime)
    {
        return Create(
            EntityKind.Topic,
            properties.Name,
            null,
            properties.Status.ToString(),
            runtime?.CreatedAt,
            runtime?.UpdatedAt,
            null,
            null,
            properties.DefaultMessageTimeToLive,
            null,
            properties.RequiresDuplicateDetection);
    }

    public static DiscoveredEntity Subscription(
        string topicName,
        SubscriptionProperties properties,
        SubscriptionRuntimeProperties? runtime)
    {
        return Create(
            EntityKind.Subscription,
            properties.SubscriptionName,
            topicName,
            properties.Status.ToString(),
            runtime?.CreatedAt,
            runtime?.UpdatedAt,
            properties.LockDuration,
            properties.MaxDeliveryCount,
            properties.DefaultMessageTimeToLive,
            properties.RequiresSession,
            null);
    }

    private static DiscoveredEntity Create(
        EntityKind kind,
        string name,
        string? topicName,
        string status,
        DateTimeOffset? createdAt,
        DateTimeOffset? updatedAt,
        TimeSpan? lockDuration,
        int? maxDeliveryCount,
        TimeSpan? defaultMessageTimeToLive,
        bool? requiresSession,
        bool? requiresDuplicateDetection)
    {
        string path = kind == EntityKind.Subscription && !string.IsNullOrWhiteSpace(topicName)
            ? $"{topicName}/subscriptions/{name}"
            : name;

        return new DiscoveredEntity(
            kind,
            name,
            topicName,
            new EntityMetadata(
                path,
                status,
                createdAt,
                updatedAt,
                lockDuration,
                maxDeliveryCount,
                defaultMessageTimeToLive,
                requiresSession,
                requiresDuplicateDetection));
    }
}
