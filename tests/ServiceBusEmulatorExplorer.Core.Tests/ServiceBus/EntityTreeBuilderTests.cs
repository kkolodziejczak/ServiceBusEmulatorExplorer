using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class EntityTreeBuilderTests
{
    [Fact]
    public void AggregateTopicCounts_uses_authoritative_subscription_counts_and_preserves_topic_scheduled_count()
    {
        ServiceBusEntityNode topic = CreateNode(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0, scheduled: 4);
        ServiceBusEntityNode billing = CreateNode(EntityKind.Subscription, "billing", "events", active: 80, deadLetter: 3);
        ServiceBusEntityNode shipping = CreateNode(EntityKind.Subscription, "shipping", "events", active: 25, deadLetter: 2);

        IReadOnlyList<ServiceBusEntityNode> aggregated = EntityTreeBuilder.AggregateTopicCounts([topic, billing, shipping]);

        EntityRuntimeCounts counts = Assert.Single(aggregated, entity => entity.Kind == EntityKind.Topic).Counts;
        Assert.Equal(105, counts.ActiveMessageCount);
        Assert.Equal(5, counts.DeadLetterMessageCount);
        Assert.Equal(4, counts.ScheduledMessageCount);
        Assert.Equal(114, counts.TotalMessageCount);
    }

    [Fact]
    public void CreateNode_maps_runtime_counts_metadata_and_subscription_path()
    {
        var createdAt = new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 7, 8, 11, 0, 0, TimeSpan.Zero);

        ServiceBusEntityNode node = EntityTreeBuilder.CreateNode(new EntityTreeSource(
            EntityKind.Subscription,
            "payments",
            "orders",
            ActiveMessageCount: 7,
            DeadLetterMessageCount: 2,
            ScheduledMessageCount: 1,
            Status: "Active",
            createdAt,
            updatedAt,
            TimeSpan.FromMinutes(2),
            MaxDeliveryCount: 5,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            RequiresSession: true,
            RequiresDuplicateDetection: false));

        Assert.Equal(EntityKind.Subscription, node.Kind);
        Assert.Equal("payments", node.Name);
        Assert.Equal("orders", node.TopicName);
        Assert.Equal(7, node.Counts.ActiveMessageCount);
        Assert.Equal(2, node.Counts.DeadLetterMessageCount);
        Assert.Equal(1, node.Counts.ScheduledMessageCount);
        Assert.Equal(10, node.Counts.TotalMessageCount);
        Assert.Equal("orders/subscriptions/payments", node.Metadata.Path);
        Assert.Equal("Active", node.Metadata.Status);
        Assert.Equal(createdAt, node.Metadata.CreatedAtUtc);
        Assert.Equal(updatedAt, node.Metadata.UpdatedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(2), node.Metadata.LockDuration);
        Assert.Equal(5, node.Metadata.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromDays(14), node.Metadata.DefaultMessageTimeToLive);
        Assert.True(node.Metadata.RequiresSession);
        Assert.False(node.Metadata.RequiresDuplicateDetection);
    }

    [Fact]
    public void SortForNavigation_orders_queues_topics_and_then_subscriptions_by_topic()
    {
        ServiceBusEntityNode topic = CreateNode(EntityKind.Topic, "events", topicName: null);
        ServiceBusEntityNode queue = CreateNode(EntityKind.Queue, "orders", topicName: null);
        ServiceBusEntityNode subscription = CreateNode(EntityKind.Subscription, "billing", "events");

        IReadOnlyList<ServiceBusEntityNode> sorted = EntityTreeBuilder.SortForNavigation([subscription, topic, queue]);

        Assert.Collection(
            sorted,
            node => Assert.Equal(queue, node),
            node => Assert.Equal(topic, node),
            node => Assert.Equal(subscription, node));
    }

    private static ServiceBusEntityNode CreateNode(
        EntityKind kind,
        string name,
        string? topicName,
        long active = 0,
        long deadLetter = 0,
        long scheduled = 0)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            ActiveMessageCount: active,
            DeadLetterMessageCount: deadLetter,
            ScheduledMessageCount: scheduled,
            Status: "Active",
            CreatedAtUtc: null,
            UpdatedAtUtc: null,
            LockDuration: null,
            MaxDeliveryCount: null,
            DefaultMessageTimeToLive: null,
            RequiresSession: null,
            RequiresDuplicateDetection: null));
    }
}
