using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class ReplayDeliverySummaryTests
{
    [Theory]
    [InlineData(true, "2 replay IDs · 4 deliveries across 2 subscriptions")]
    [InlineData(false, "2 replay IDs · 4 deliveries across 2 subscriptions · found so far")]
    public void Counts_fanout_deliveries_separately_from_replay_ids(bool complete, string expected)
    {
        var first = new EntityAddress(EntityKind.Subscription, "storefront", "inventory");
        var second = new EntityAddress(EntityKind.Subscription, "warehouse", "inventory");
        Assert.Equal(expected, ReplayDeliverySummary.Format(
            [Row("replay-1", first), Row("replay-1", second), Row("replay-2", first), Row("replay-2", second)], complete));
    }

    [Fact]
    public void Counts_same_named_subscriptions_on_different_topics_separately_and_keeps_buckets_as_deliveries()
    {
        var first = new EntityAddress(EntityKind.Subscription, "billing", "orders");
        var second = new EntityAddress(EntityKind.Subscription, "billing", "returns");
        Assert.Equal("1 replay ID · 3 deliveries across 2 subscriptions", ReplayDeliverySummary.Format(
            [Row("replay", first), Row("replay", second), Row("replay", first, MessageBucket.DeadLetter)], true));
    }

    [Fact]
    public void Supports_queue_and_empty_results_without_claiming_subscription_delivery()
    {
        Assert.Equal("1 replay ID · 1 delivery across 1 queue", ReplayDeliverySummary.Format(
            [Row("replay", new EntityAddress(EntityKind.Queue, "orders"))], true));
        Assert.Equal("0 replay IDs · 0 deliveries across 0 receiving entities · found so far",
            ReplayDeliverySummary.Format([], false));
    }

    private static MessageRow Row(string id, EntityAddress source, MessageBucket bucket = MessageBucket.Active) =>
        new(new MessageDelivery(new DeliveryIdentity(1, source, bucket, 1),
            new ExplorerMessage(id, 1, "{}", id, 2, null, null, 0, "application/json", null, null, "OrderPlaced",
                new Dictionary<string, object?>(), new Dictionary<string, object?>())), TimestampDisplay.Utc);
}
