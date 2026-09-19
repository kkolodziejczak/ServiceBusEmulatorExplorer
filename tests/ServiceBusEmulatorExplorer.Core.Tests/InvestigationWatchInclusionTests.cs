using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationWatchInclusionTests
{
    [Fact]
    public void Inclusion_defaults_to_true_without_enabling_unconfigured_buckets()
    {
        var snapshot = Snapshot(Queue("audit"));
        var resolver = new WatchScopeResolver();

        Assert.Equal(Array.Empty<WatchTarget>(), resolver.Resolve([], snapshot));
        Assert.Equal(
            new[] { QueueTarget("audit", MessageBucket.DeadLetter) },
            resolver.Resolve([new(WatchScopeResolver.ConnectionScopeKey, false, true)], snapshot));
    }

    [Theory]
    [InlineData("connection:*")]
    [InlineData("category:topics")]
    [InlineData("topic:orders")]
    [InlineData("subscription:orders/billing")]
    public void Exclusion_at_each_scope_suppresses_even_explicit_leaf_bucket_rules(string excludedScope)
    {
        WatchPreference[] preferences =
        [
            new(WatchScopeResolver.ConnectionScopeKey, true, true),
            new(excludedScope, null, null, Included: false),
            new(WatchScopeResolver.SubscriptionScopeKey("orders", "billing"), true, true)
        ];

        Assert.Equal(
            Array.Empty<WatchTarget>(),
            new WatchScopeResolver().Resolve(preferences, Snapshot(Subscription("orders", "billing"))));
    }

    [Fact]
    public void Queue_category_exclusion_allows_only_explicitly_reincluded_queue()
    {
        WatchPreference[] preferences =
        [
            new(WatchScopeResolver.ConnectionScopeKey, true, true),
            new(WatchScopeResolver.QueueCategoryScopeKey, null, null, Included: false),
            new(WatchScopeResolver.QueueScopeKey("audit"), null, null, Included: true)
        ];

        Assert.Equal(
            new[] { QueueTarget("audit", MessageBucket.Active), QueueTarget("audit", MessageBucket.DeadLetter) },
            new WatchScopeResolver().Resolve(preferences, Snapshot(Queue("audit"), Queue("excluded"))));
    }

    [Fact]
    public void Category_topic_and_leaf_can_each_reopen_inclusion_without_resetting_bucket_rules()
    {
        WatchPreference[] preferences =
        [
            new(WatchScopeResolver.ConnectionScopeKey, true, false, Included: false),
            new(WatchScopeResolver.TopicCategoryScopeKey, null, null, Included: true),
            new(WatchScopeResolver.TopicScopeKey("orders"), false, true, Included: false),
            new(WatchScopeResolver.SubscriptionScopeKey("orders", "billing"), null, null, Included: true),
            new(WatchScopeResolver.TopicScopeKey("reopened"), null, null, Included: true)
        ];

        Assert.Equal(
            new[]
            {
                SubscriptionTarget("other", "category", MessageBucket.Active),
                SubscriptionTarget("orders", "billing", MessageBucket.DeadLetter),
                SubscriptionTarget("reopened", "shipping", MessageBucket.Active)
            },
            new WatchScopeResolver().Resolve(preferences, Snapshot(
                Queue("excluded"), Subscription("other", "category"),
                Subscription("orders", "billing"), Subscription("orders", "excluded"),
                Subscription("reopened", "shipping"))));
    }

    [Fact]
    public void Topic_reinclusion_overrides_excluded_category_for_future_subscriptions()
    {
        WatchPreference[] preferences =
        [
            new(WatchScopeResolver.ConnectionScopeKey, true, false),
            new(WatchScopeResolver.TopicCategoryScopeKey, null, null, Included: false),
            new(WatchScopeResolver.TopicScopeKey("orders"), null, null, Included: true)
        ];
        var resolver = new WatchScopeResolver();

        Assert.Equal(Array.Empty<WatchTarget>(), resolver.Resolve(preferences, Snapshot()));
        Assert.Equal(
            new[] { SubscriptionTarget("orders", "future", MessageBucket.Active) },
            resolver.Resolve(preferences, Snapshot(Subscription("orders", "future"), Subscription("other", "future"))));
    }

    [Fact]
    public void Duplicate_scope_entries_take_last_non_null_value_for_each_field_independently()
    {
        string key = WatchScopeResolver.QueueScopeKey("audit");
        WatchPreference[] preferences =
        [
            new(WatchScopeResolver.ConnectionScopeKey, true, false, Included: false),
            new(key, null, true, Included: false),
            new(key, false, null, Included: true),
            new(key, null, null, Included: null)
        ];

        Assert.Equal(
            new[] { QueueTarget("audit", MessageBucket.DeadLetter) },
            new WatchScopeResolver().Resolve(preferences, Snapshot(Queue("audit"))));
    }

    private static WatchTarget QueueTarget(string name, MessageBucket bucket)
        => new(new(EntityKind.Queue, name), bucket);

    private static WatchTarget SubscriptionTarget(string topic, string name, MessageBucket bucket)
        => new(new(EntityKind.Subscription, name, topic), bucket);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities)
        => new(
            entities.Select(entity => new EntityObservation(entity, new EntityCountObservation(
                new(null, CountAvailability.Unavailable),
                new(null, CountAvailability.Unavailable),
                new(null, CountAvailability.NotSupported)))).ToArray(),
            DateTimeOffset.UtcNow, IsComplete: true, Issues: []);

    private static ServiceBusEntityNode Queue(string name)
        => Entity(EntityKind.Queue, name, null);

    private static ServiceBusEntityNode Subscription(string topic, string name)
        => Entity(EntityKind.Subscription, name, topic);

    private static ServiceBusEntityNode Entity(EntityKind kind, string name, string? topic)
        => new(kind, name, topic, new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));
}
