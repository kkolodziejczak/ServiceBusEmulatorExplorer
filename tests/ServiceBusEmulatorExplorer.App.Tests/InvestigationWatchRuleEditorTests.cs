using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationWatchRuleEditorTests
{
    [Fact]
    public void Inclusion_is_independent_of_bucket_choices_and_mixed_parents_reflect_leaves()
    {
        var tree = new Tree();
        var editor = new WatchRuleEditor(tree.Roots, []);
        Assert.True(editor.GetIncludedState(tree.Topics));
        Assert.Empty(editor.Targets);

        editor.SetGlobal(false, true);
        editor.SetGlobal(true, true);
        editor.SetIncluded(tree.First, false);

        Assert.Null(editor.GetIncludedState(tree.Topic));
        Assert.Null(editor.GetIncludedState(tree.Topics));
        Assert.False(editor.IsWatched(tree.First, false));
        Assert.False(editor.IsWatched(tree.First, true));
        Assert.True(editor.IsWatched(tree.Second, false));
        Assert.True(editor.IsWatched(tree.Second, true));
        Assert.Equal(4, editor.Targets.Count);
        Assert.DoesNotContain(editor.Targets, target => target.Address.Kind == EntityKind.Topic);
    }

    [Fact]
    public void Enabling_global_bucket_clears_false_overrides_only_for_that_bucket()
    {
        var tree = new Tree();
        var editor = new WatchRuleEditor(tree.Roots, []);
        editor.SetGlobal(true, true);
        editor.SetScope(tree.First, false, false);
        editor.SetScope(tree.First, true, false);
        editor.SetIncluded(tree.Queue, false);

        editor.SetGlobal(false, true);

        Assert.True(editor.GlobalActive);
        Assert.True(editor.GlobalDeadLetter);
        Assert.True(editor.IsWatched(tree.First, false));
        Assert.False(editor.IsWatched(tree.First, true));
        Assert.False(editor.IsWatched(tree.Queue, false));
        Assert.False(editor.GetIncludedState(tree.Queue));
    }

    [Fact]
    public void Disabling_global_bucket_retains_explicit_leaf_choices()
    {
        var tree = new Tree();
        var editor = new WatchRuleEditor(tree.Roots, []);
        editor.SetGlobal(false, true);
        editor.SetScope(tree.First, false, true);
        editor.SetGlobal(false, false);

        Assert.False(editor.GlobalActive);
        Assert.True(editor.IsWatched(tree.First, false));
        Assert.False(editor.IsWatched(tree.Second, false));
        Assert.Equal(tree.First.Address, Assert.Single(editor.Targets).Address);
    }

    [Fact]
    public void Branch_inclusion_replaces_child_exceptions_and_applies_to_future_subscriptions()
    {
        var tree = new Tree();
        var editor = new WatchRuleEditor(tree.Roots, []);
        editor.SetGlobal(false, true);
        editor.SetGlobal(true, true);
        editor.SetIncluded(tree.Topic, false);
        editor.SetIncluded(tree.First, true);
        editor.SetScope(tree.First, false, false);
        editor.SetScope(tree.Second, true, false);
        Assert.Null(editor.GetIncludedState(tree.Topic));

        editor.SetIncluded(tree.Topic, true);

        Assert.True(editor.GetIncludedState(tree.Topic));
        Assert.True(editor.IsWatched(tree.First, false));
        Assert.True(editor.IsWatched(tree.Second, true));
        editor.SetIncluded(tree.Topic, false);
        var future = Entity(EntityKind.Subscription, "future", tree.Topic.Name);
        tree.Topic.Children.Add(future);
        var restored = new WatchRuleEditor(tree.Roots, editor.Rules);
        Assert.False(restored.GetIncludedState(tree.Topic));
        Assert.False(restored.IsWatched(tree.First, false));
        Assert.False(restored.IsWatched(future, true));
        Assert.True(restored.IsWatched(tree.Queue, false));
    }

    [Fact]
    public void Topic_bucket_choice_replaces_child_choices_without_changing_other_bucket_or_inclusion()
    {
        var tree = new Tree();
        var editor = new WatchRuleEditor(tree.Roots, []);
        editor.SetScope(tree.First, false, false);
        editor.SetScope(tree.First, true, true);
        editor.SetIncluded(tree.Second, false);

        editor.SetScope(tree.Topic, false, true);
        var future = Entity(EntityKind.Subscription, "future", tree.Topic.Name);
        tree.Topic.Children.Add(future);
        var restored = new WatchRuleEditor(tree.Roots, editor.Rules);

        Assert.True(restored.IsWatched(tree.First, false));
        Assert.True(restored.IsWatched(tree.First, true));
        Assert.False(restored.IsWatched(tree.Second, false));
        Assert.True(restored.IsWatched(future, false));
        Assert.False(restored.IsWatched(future, true));
        restored.SetScope(tree.Topic, false, false);
        Assert.False(restored.IsWatched(tree.First, false));
        Assert.True(restored.IsWatched(tree.First, true));
    }

    [Fact]
    public void Queue_and_topic_with_same_name_have_distinct_rules()
    {
        var tree = new Tree();
        Assert.Equal(tree.Queue.Name, tree.Topic.Name);
        var editor = new WatchRuleEditor(tree.Roots, []);
        editor.SetScope(tree.Queue, false, true);
        editor.SetScope(tree.Topic, true, true);
        editor.SetIncluded(tree.Queue, false);

        Assert.False(editor.IsWatched(tree.Queue, false));
        Assert.True(editor.IsWatched(tree.First, true));
        Assert.True(editor.GetIncludedState(tree.Topic));
        Assert.False(editor.IsWatched(tree.First, false));
        Assert.Contains(editor.Rules, rule => rule.ScopeKey == "queue:orders");
        Assert.Contains(editor.Rules, rule => rule.ScopeKey == "topic:orders");
    }

    [Fact]
    public void Duplicate_saved_rules_use_last_non_null_field_and_edits_preserve_other_fields()
    {
        var tree = new Tree();
        WatchPreference[] saved =
        [
            new("connection:*", true, false),
            new("connection:*", null, true),
            new("subscription:orders/first", false, true, false),
            new("subscription:orders/first", true, null, null),
            new("subscription:orders/first", null, null, true)
        ];
        var editor = new WatchRuleEditor(tree.Roots, saved);
        Assert.True(editor.GlobalActive);
        Assert.True(editor.GlobalDeadLetter);
        Assert.True(editor.IsWatched(tree.First, false));
        Assert.True(editor.IsWatched(tree.First, true));

        editor.SetScope(tree.First, false, false);
        var restored = new WatchRuleEditor(tree.Roots, editor.Rules);
        Assert.False(restored.IsWatched(tree.First, false));
        Assert.True(restored.IsWatched(tree.First, true));
        Assert.True(restored.GetIncludedState(tree.First));
        Assert.Equal(5, saved.Length);
        Assert.True(saved[3].Active);
    }

    [Fact]
    public void Category_exclusion_covers_future_queues_without_affecting_topics()
    {
        var tree = new Tree();
        var editor = new WatchRuleEditor(tree.Roots, []);
        editor.SetGlobal(true, true);
        editor.SetIncluded(tree.Queues, false);
        var future = Entity(EntityKind.Queue, "future");
        tree.Queues.Children.Add(future);
        var restored = new WatchRuleEditor(tree.Roots, editor.Rules);

        Assert.False(restored.IsWatched(future, true));
        Assert.False(restored.IsWatched(tree.Queue, true));
        Assert.True(restored.IsWatched(tree.First, true));
        Assert.Contains(restored.Rules, rule => rule.ScopeKey == "category:queues" && rule.Included == false);
    }

    [Fact]
    public void Topic_bucket_change_does_not_change_a_distinct_topic_with_a_slash_in_its_name()
    {
        var topics = new EntityNode("Topics", "Group");
        var orders = Entity(EntityKind.Topic, "orders");
        var archive = Entity(EntityKind.Topic, "orders/archive");
        var consumer = Entity(EntityKind.Subscription, "consumer", orders.Name);
        var archiveConsumer = Entity(EntityKind.Subscription, "consumer", archive.Name);
        topics.Children.Add(orders);
        topics.Children.Add(archive);
        orders.Children.Add(consumer);
        archive.Children.Add(archiveConsumer);
        var editor = new WatchRuleEditor([topics], []);
        editor.SetGlobal(false, true);
        editor.SetScope(consumer, false, false);
        editor.SetScope(archiveConsumer, false, false);

        editor.SetScope(orders, false, true);

        Assert.True(editor.IsWatched(consumer, false));
        Assert.False(editor.IsWatched(archiveConsumer, false));
        var restored = new WatchRuleEditor([topics], editor.Rules);
        Assert.False(restored.IsWatched(archiveConsumer, false));
        Assert.Equal(consumer.Address, Assert.Single(restored.Targets).Address);
    }

    [Fact]
    public void Topic_inclusion_change_preserves_exclusions_on_a_distinct_topic_with_a_slash_in_its_name()
    {
        var topics = new EntityNode("Topics", "Group");
        var orders = Entity(EntityKind.Topic, "orders");
        var archive = Entity(EntityKind.Topic, "orders/archive");
        var consumer = Entity(EntityKind.Subscription, "consumer", orders.Name);
        var archiveConsumer = Entity(EntityKind.Subscription, "consumer", archive.Name);
        topics.Children.Add(orders);
        topics.Children.Add(archive);
        orders.Children.Add(consumer);
        archive.Children.Add(archiveConsumer);
        var editor = new WatchRuleEditor([topics], []);
        editor.SetGlobal(false, true);
        editor.SetGlobal(true, true);
        editor.SetIncluded(consumer, false);
        editor.SetIncluded(archiveConsumer, false);
        editor.SetScope(archiveConsumer, false, false);

        editor.SetIncluded(orders, true);

        Assert.True(editor.GetIncludedState(consumer));
        Assert.False(editor.GetIncludedState(archiveConsumer));
        Assert.True(editor.IsWatched(consumer, true));
        Assert.False(editor.IsWatched(archiveConsumer, true));
        Assert.Contains(editor.Rules, rule => rule.ScopeKey == "subscription:orders/archive/consumer"
            && rule.Active == false);
        var restored = new WatchRuleEditor([topics], editor.Rules);
        Assert.False(restored.GetIncludedState(archiveConsumer));
    }

    private sealed class Tree
    {
        public EntityNode Queues { get; } = new("Queues", "Group");
        public EntityNode Topics { get; } = new("Topics", "Group");
        public EntityNode Queue { get; } = Entity(EntityKind.Queue, "orders");
        public EntityNode Topic { get; } = Entity(EntityKind.Topic, "orders");
        public EntityNode First { get; } = Entity(EntityKind.Subscription, "first", "orders");
        public EntityNode Second { get; } = Entity(EntityKind.Subscription, "second", "orders");
        public EntityNode[] Roots => [Queues, Topics];

        public Tree()
        {
            Queues.Children.Add(Queue);
            Topics.Children.Add(Topic);
            Topic.Children.Add(First);
            Topic.Children.Add(Second);
        }
    }

    private static EntityNode Entity(EntityKind kind, string name, string? topicName = null)
    {
        var entity = new DiscoveredEntity(kind, name, topicName,
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));
        var counts = new EntityCountObservation(new(0, CountAvailability.Known),
            new(0, CountAvailability.Known), new(0, CountAvailability.Known));
        return new EntityNode(name, kind.ToString(), new EntityObservation(entity, counts));
    }
}
