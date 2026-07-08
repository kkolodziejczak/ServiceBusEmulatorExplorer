using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class EntityTreeBuilderTests
{
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

    private static ServiceBusEntityNode CreateNode(EntityKind kind, string name, string? topicName)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            ActiveMessageCount: 0,
            DeadLetterMessageCount: 0,
            ScheduledMessageCount: 0,
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
