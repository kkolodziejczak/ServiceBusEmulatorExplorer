namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public sealed record EntityTreeSource(
    EntityKind Kind,
    string Name,
    string? TopicName,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long ScheduledMessageCount,
    string Status,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    TimeSpan? LockDuration,
    int? MaxDeliveryCount,
    TimeSpan? DefaultMessageTimeToLive,
    bool? RequiresSession,
    bool? RequiresDuplicateDetection);

public static class EntityTreeBuilder
{
    public static ServiceBusEntityNode CreateNode(EntityTreeSource source)
    {
        var counts = new EntityRuntimeCounts(
            source.ActiveMessageCount,
            source.DeadLetterMessageCount,
            source.ScheduledMessageCount,
            source.ActiveMessageCount + source.DeadLetterMessageCount + source.ScheduledMessageCount);

        var metadata = new EntityMetadata(
            CreatePath(source),
            source.Status,
            source.CreatedAtUtc,
            source.UpdatedAtUtc,
            source.LockDuration,
            source.MaxDeliveryCount,
            source.DefaultMessageTimeToLive,
            source.RequiresSession,
            source.RequiresDuplicateDetection);

        return new ServiceBusEntityNode(
            source.Kind,
            source.Name,
            source.TopicName,
            counts,
            metadata);
    }

    private static string CreatePath(EntityTreeSource source)
    {
        if (source.Kind == EntityKind.Subscription)
        {
            return !string.IsNullOrWhiteSpace(source.TopicName)
                ? $"{source.TopicName}/subscriptions/{source.Name}"
                : source.Name;
        }

        return source.Name;
    }

    public static IReadOnlyList<ServiceBusEntityNode> SortForNavigation(IEnumerable<ServiceBusEntityNode> nodes)
    {
        return nodes
            .OrderBy(node => GetKindOrder(node.Kind))
            .ThenBy(node => node.TopicName ?? node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Kind == EntityKind.Subscription ? node.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetKindOrder(EntityKind kind)
    {
        return kind switch
        {
            EntityKind.Queue => 0,
            EntityKind.Topic => 1,
            EntityKind.Subscription => 2,
            _ => 3
        };
    }
}
