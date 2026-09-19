using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationWatchScopeResolverTests
{
    [Fact]
    public void Resolves_connection_category_topic_and_leaf_rules_with_child_precedence()
    {
        EntityDiscoverySnapshot snapshot = Snapshot(
            Queue("audit"),
            Topic("orders"),
            Subscription("orders", "billing"),
            Subscription("orders", "shipping"));
        var preferences = new[]
        {
            new WatchPreference(WatchScopeResolver.ConnectionScopeKey, Active: true, DeadLetter: false),
            new WatchPreference(WatchScopeResolver.QueueCategoryScopeKey, Active: false, DeadLetter: null),
            new WatchPreference(WatchScopeResolver.TopicScopeKey("orders"), Active: true, DeadLetter: true),
            new WatchPreference(WatchScopeResolver.SubscriptionScopeKey("orders", "billing"), Active: false, DeadLetter: null)
        };

        IReadOnlyList<WatchTarget> result = new WatchScopeResolver().Resolve(preferences, snapshot);

        Assert.Equal(
            new[]
            {
                Target(new EntityAddress(EntityKind.Subscription, "billing", "orders"), MessageBucket.DeadLetter),
                Target(new EntityAddress(EntityKind.Subscription, "shipping", "orders"), MessageBucket.Active),
                Target(new EntityAddress(EntityKind.Subscription, "shipping", "orders"), MessageBucket.DeadLetter)
            },
            result);
    }

    [Fact]
    public void Resolves_against_fresh_snapshots_so_future_entities_inherit_rules()
    {
        var resolver = new WatchScopeResolver();
        var preferences = new[]
        {
            new WatchPreference(WatchScopeResolver.ConnectionScopeKey, Active: false, DeadLetter: false),
            new WatchPreference(WatchScopeResolver.TopicScopeKey("orders"), Active: true, DeadLetter: null)
        };

        IReadOnlyList<WatchTarget> result = resolver.Resolve(
            preferences,
            Snapshot(Topic("orders"), Subscription("orders", "future")));

        Assert.Equal(
            new[]
            {
                Target(new EntityAddress(EntityKind.Subscription, "future", "orders"), MessageBucket.Active)
            },
            result);
    }

    [Fact]
    public void Excludes_topics_and_deduplicates_repeated_receiving_entities()
    {
        EntityAddress queue = new(EntityKind.Queue, "audit");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("audit"), Queue("audit"), Topic("orders"));
        var preferences = new[]
        {
            new WatchPreference(WatchScopeResolver.ConnectionScopeKey, Active: true, DeadLetter: true)
        };

        IReadOnlyList<WatchTarget> result = new WatchScopeResolver().Resolve(preferences, snapshot);

        Assert.Equal(
            new[] { Target(queue, MessageBucket.Active), Target(queue, MessageBucket.DeadLetter) },
            result);
    }

    [Fact]
    public void Active_and_dead_letter_rules_are_independent()
    {
        EntityAddress queue = new(EntityKind.Queue, "audit");
        var preferences = new[]
        {
            new WatchPreference(WatchScopeResolver.ConnectionScopeKey, Active: true, DeadLetter: false),
            new WatchPreference(WatchScopeResolver.QueueScopeKey("audit"), Active: null, DeadLetter: true)
        };

        IReadOnlyList<WatchTarget> result = new WatchScopeResolver().Resolve(preferences, Snapshot(Queue("audit")));

        Assert.Equal(
            new[] { Target(queue, MessageBucket.Active), Target(queue, MessageBucket.DeadLetter) },
            result);
    }

    [Fact]
    public void Tagged_keys_keep_connection_category_topic_and_leaf_names_distinct()
    {
        EntityAddress queueNamedConnection = new(EntityKind.Queue, "connection");
        EntityAddress queueNamedCategory = new(EntityKind.Queue, "queues");
        EntityAddress subscription = new(EntityKind.Subscription, "queues", "connection");
        var preferences = new[]
        {
            new WatchPreference(WatchScopeResolver.ConnectionScopeKey, Active: false, DeadLetter: false),
            new WatchPreference(WatchScopeResolver.QueueCategoryScopeKey, Active: true, DeadLetter: null),
            new WatchPreference(WatchScopeResolver.TopicCategoryScopeKey, Active: false, DeadLetter: null),
            new WatchPreference(WatchScopeResolver.QueueScopeKey("connection"), Active: false, DeadLetter: null),
            new WatchPreference(WatchScopeResolver.TopicScopeKey("connection"), Active: true, DeadLetter: null),
            new WatchPreference(WatchScopeResolver.SubscriptionScopeKey("connection", "queues"), Active: true, DeadLetter: null),
            // Raw prototype paths are invalid in the tagged production schema.
            new WatchPreference("connection", Active: true, DeadLetter: null),
            new WatchPreference("queues", Active: false, DeadLetter: null)
        };

        IReadOnlyList<WatchTarget> result = new WatchScopeResolver().Resolve(
            preferences,
            Snapshot(Queue("connection"), Queue("queues"), Topic("connection"), Subscription("connection", "queues")));

        Assert.Equal(
            new[]
            {
                Target(queueNamedCategory, MessageBucket.Active),
                Target(subscription, MessageBucket.Active)
            },
            result);
        Assert.DoesNotContain(result, target => target.Address == queueNamedConnection);
    }

    private static WatchTarget Target(EntityAddress address, MessageBucket bucket) => new(address, bucket);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities)
        => new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(null, CountAvailability.Unavailable),
                    new(null, CountAvailability.Unavailable),
                    new(null, CountAvailability.NotSupported)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);

    private static ServiceBusEntityNode Queue(string name)
        => new(
            EntityKind.Queue,
            name,
            null,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));

    private static ServiceBusEntityNode Topic(string name)
        => new(
            EntityKind.Topic,
            name,
            null,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));

    private static ServiceBusEntityNode Subscription(string topicName, string name)
        => new(
            EntityKind.Subscription,
            name,
            topicName,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata($"{topicName}/Subscriptions/{name}", "Active", null, null, null, null, null, null, null));
}
