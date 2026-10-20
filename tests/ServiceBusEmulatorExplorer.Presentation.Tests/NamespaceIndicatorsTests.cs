using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class NamespaceIndicatorsTests
{
    private static readonly EntityAddress QueueAddress = new(EntityKind.Queue, "checkout-requests");
    private static readonly EntityAddress BillingAddress = new(EntityKind.Subscription, "billing", "order-events");

    [Fact]
    public void A_queue_watched_in_both_buckets_refreshes_in_the_background()
    {
        var tree = new Tree();

        Apply(tree, [new(QueueAddress, MessageBucket.Active), new(QueueAddress, MessageBucket.DeadLetter)], Refresh());

        Assert.True(tree.Queue.IsActiveWatched);
        Assert.True(tree.Queue.IsDlqWatched);
        Assert.Equal("Running", tree.Queue.RefreshIndicator);
        Assert.Equal("Refresh: Every 30 seconds\nBackground: Active and Dead letter. Watch remains independent.", tree.Queue.RefreshDetail);
        Assert.Equal("None", tree.Billing.RefreshIndicator);
        Assert.Equal("", tree.Billing.RefreshDetail);
    }

    [Fact]
    public void A_topic_refreshes_for_its_watched_subscriptions_without_being_watched_itself()
    {
        var tree = new Tree();

        Apply(tree, [new(BillingAddress, MessageBucket.DeadLetter)], Refresh());

        Assert.False(tree.Topic.IsActiveWatched || tree.Topic.IsDlqWatched);
        Assert.True(tree.Billing.IsDlqWatched);
        Assert.False(tree.Billing.IsActiveWatched);
        Assert.Equal("Running", tree.Topic.RefreshIndicator);
        Assert.Equal("Refresh: Every 30 seconds\nBackground: Dead letter watched subscriptions. Watch remains independent.", tree.Topic.RefreshDetail);
        Assert.Equal("Refresh: Every 30 seconds\nBackground: Dead letter. Watch remains independent.", tree.Billing.RefreshDetail);

        Apply(tree, [new(tree.Topic.Address!, MessageBucket.Active)], Refresh());

        Assert.False(tree.Topic.IsActiveWatched);
    }

    [Fact]
    public void The_entity_on_screen_refreshes_and_says_which_view_is_refreshed()
    {
        var tree = new Tree();

        Apply(tree, [], Refresh(deadLetterView: true, current: BillingAddress));

        Assert.Equal("Running", tree.Billing.RefreshIndicator);
        Assert.Equal("Refresh: Every 30 seconds\nCurrent Dead letter view. Watch remains independent.", tree.Billing.RefreshDetail);
        Assert.Equal("None", tree.Queue.RefreshIndicator);
        Assert.Equal("None", tree.Queues.RefreshIndicator);
    }

    [Fact]
    public void Paused_refresh_says_so()
    {
        var tree = new Tree();

        Apply(tree, [], Refresh(paused: true, current: QueueAddress));

        Assert.Equal("Paused", tree.Queue.RefreshIndicator);
        Assert.Equal("Auto-refresh paused: Every 30 seconds\nCurrent Active view. Watch remains independent.", tree.Queue.RefreshDetail);
    }

    [Fact]
    public void Without_automatic_refresh_or_a_connection_only_the_watch_marks_remain()
    {
        var tree = new Tree();
        WatchTarget[] watched = [new(QueueAddress, MessageBucket.Active)];
        Apply(tree, watched, Refresh(current: QueueAddress));
        Assert.Equal("Running", tree.Queue.RefreshIndicator);
        Assert.NotEqual("", tree.Queue.RefreshDetail);

        Apply(tree, watched, Refresh(seconds: 0, current: QueueAddress));
        Assert.Equal("None", tree.Queue.RefreshIndicator);
        Assert.Equal("", tree.Queue.RefreshDetail);
        Assert.True(tree.Queue.IsActiveWatched);

        Apply(tree, watched, Refresh(current: QueueAddress));
        Apply(tree, watched, Refresh(connected: false, current: QueueAddress));
        Assert.Equal("", tree.Queue.RefreshDetail);
        Assert.Equal("None", tree.Queue.RefreshIndicator);
        Assert.True(tree.Queue.IsActiveWatched);
        Assert.Contains("Disconnected; Watch resumes after reconnecting.", tree.Queue.ActiveCountDetail, StringComparison.Ordinal);
    }

    private static NamespaceRefresh Refresh(bool connected = true, int seconds = 30, bool paused = false,
        bool deadLetterView = false, EntityAddress? current = null) => new(connected, seconds, paused, deadLetterView, current);

    private static void Apply(Tree tree, IEnumerable<WatchTarget> targets, NamespaceRefresh refresh) =>
        NamespaceIndicators.Apply(tree.Roots.SelectMany(WatchRuleEditor.Flatten), targets, refresh);

    private sealed class Tree
    {
        public Tree()
        {
            Queues.Children.Add(Queue);
            Topic.Children.Add(Billing);
            Topics.Children.Add(Topic);
        }

        public EntityNode Queues { get; } = new("Queues", "QueueGroup");
        public EntityNode Topics { get; } = new("Topics", "TopicGroup");
        public EntityNode Queue { get; } = Entity(EntityKind.Queue, "checkout-requests");
        public EntityNode Topic { get; } = Entity(EntityKind.Topic, "order-events");
        public EntityNode Billing { get; } = Entity(EntityKind.Subscription, "billing", "order-events");
        public EntityNode[] Roots => [Queues, Topics];

        private static EntityNode Entity(EntityKind kind, string name, string? topic = null) => new(name, kind.ToString(),
            new EntityObservation(new DiscoveredEntity(kind, name, topic,
                new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
                new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known))));
    }
}
