using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class GlobalWatchViewModelTests
{
    [Fact]
    public void Requires_entities_rules_and_a_way_to_save_them()
    {
        var tree = new Tree();
        Assert.Equal("roots", Assert.Throws<ArgumentNullException>(() => new GlobalWatchViewModel(null!, tree.Rules(), _ => Task.FromResult(true))).ParamName);
        Assert.Equal("rules", Assert.Throws<ArgumentNullException>(() => new GlobalWatchViewModel(tree.Roots, null!, _ => Task.FromResult(true))).ParamName);
        Assert.Equal("saveAsync", Assert.Throws<ArgumentNullException>(() => new GlobalWatchViewModel(tree.Roots, tree.Rules(), null!)).ParamName);
    }

    [Fact]
    public void Opens_with_the_saved_choices_and_every_entity_with_its_included_state()
    {
        var tree = new Tree();
        WatchRuleEditor rules = tree.Rules();
        rules.SetGlobal(deadLetter: true, enabled: true);
        rules.SetIncluded(tree.Billing, false);

        var watch = new GlobalWatchViewModel(tree.Roots, rules, _ => Task.FromResult(true));

        Assert.False(watch.GlobalActive);
        Assert.True(watch.GlobalDeadLetter);
        Assert.True(watch.IsEditable);
        Assert.False(watch.HasNoMatches);
        Assert.Equal([WatchScopeResolver.QueueCategoryScopeKey, WatchScopeResolver.TopicCategoryScopeKey], watch.Items.Select(item => item.Key));
        WatchEntityItem topic = watch.Items[1].Children.Single();
        Assert.Null(topic.IsIncluded);
        Assert.Equal([false, true], topic.Children.Select(item => item.IsIncluded));
        Assert.True(topic.IsEmphasized);
        Assert.True(watch.Items[0].IsEmphasized);
        Assert.False(watch.Items[0].Children[0].IsEmphasized);
        Assert.All(topic.Children, subscription => Assert.False(subscription.IsEmphasized));
    }

    [Fact]
    public void Turning_a_global_choice_on_saves_it()
    {
        var tree = new Tree();
        var saved = new List<IReadOnlyList<WatchPreference>>();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), rules => { saved.Add(rules.Rules); return Task.FromResult(true); });

        _ = watch.SetGlobalAsync(deadLetter: false, enabled: true);

        Assert.True(watch.GlobalActive);
        Assert.False(watch.GlobalDeadLetter);
        Assert.True(new WatchRuleEditor(tree.Roots, Assert.Single(saved)).GlobalActive);
    }

    [Fact]
    public void Including_a_topic_includes_its_whole_branch_and_saves_it()
    {
        var tree = new Tree();
        WatchRuleEditor rules = tree.Rules();
        rules.SetIncluded(tree.Billing, false);
        WatchRuleEditor? saved = null;
        var watch = new GlobalWatchViewModel(tree.Roots, rules, edited => { saved = edited; return Task.FromResult(true); });

        _ = watch.SetIncludedAsync(tree.Topic, true);

        WatchEntityItem topic = watch.Items[1].Children.Single();
        Assert.True(topic.IsIncluded);
        Assert.All(topic.Children, item => Assert.True(item.IsIncluded));
        Assert.Equal(true, saved!.GetIncludedState(tree.Billing));
    }

    [Fact]
    public void While_a_change_is_saving_nothing_else_can_be_changed_or_applied()
    {
        var tree = new Tree();
        var pending = new TaskCompletionSource<bool>();
        int saves = 0;
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => { saves++; return pending.Task; });

        Task saving = watch.SetGlobalAsync(deadLetter: false, enabled: true);
        _ = watch.SetGlobalAsync(deadLetter: true, enabled: true);
        _ = watch.SetIncludedAsync(tree.Billing, false);
        WatchRuleEditor external = tree.Rules();
        external.SetIncluded(tree.Queue, false);
        watch.Refresh(tree.Roots, external);

        Assert.False(watch.IsEditable);
        Assert.Equal(1, saves);
        pending.SetResult(true);
        Assert.True(saving.IsCompletedSuccessfully);
        Assert.True(watch.IsEditable);
        Assert.True(watch.GlobalActive);
        Assert.False(watch.GlobalDeadLetter);
        Assert.Equal(true, watch.Items[0].Children.Single().IsIncluded);
    }

    [Fact]
    public void A_failed_save_puts_back_the_last_saved_rules()
    {
        var tree = new Tree();
        bool accept = true;
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => Task.FromResult(accept));
        _ = watch.SetGlobalAsync(deadLetter: true, enabled: true);

        accept = false;
        _ = watch.SetGlobalAsync(deadLetter: false, enabled: true);
        _ = watch.SetIncludedAsync(tree.Billing, false);

        Assert.False(watch.GlobalActive);
        Assert.True(watch.GlobalDeadLetter);
        Assert.Equal(true, watch.Items[1].Children.Single().Children[0].IsIncluded);
        Assert.True(watch.IsEditable);
    }

    [Fact]
    public void Shown_choices_follow_a_change_even_when_the_save_fails()
    {
        var tree = new Tree();
        var announced = new List<string?>();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => Task.FromResult(false));
        watch.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        _ = watch.SetGlobalAsync(deadLetter: false, enabled: true);

        Assert.Contains(nameof(GlobalWatchViewModel.GlobalActive), announced);
        Assert.Contains(nameof(GlobalWatchViewModel.Items), announced);
        Assert.Equal([nameof(GlobalWatchViewModel.IsEditable)], announced.Take(1));
    }

    [Fact]
    public void Search_keeps_matching_entities_with_their_whole_branch_and_their_ancestors()
    {
        var tree = new Tree();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => Task.FromResult(true));

        watch.SearchText = "  BILLING ";

        WatchEntityItem topics = Assert.Single(watch.Items);
        Assert.Equal(["billing"], topics.Children.Single().Children.Select(item => item.Node.Name));

        watch.SearchText = "order-events";

        Assert.Equal(["billing", "audit"], Assert.Single(watch.Items).Children.Single().Children.Select(item => item.Node.Name));

        watch.SearchText = "topics";

        WatchEntityItem matchedGroup = Assert.Single(watch.Items);
        Assert.Equal(["billing", "audit"], matchedGroup.Children.Single().Children.Select(item => item.Node.Name));

        var announced = new List<string?>();
        watch.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        watch.SearchText = "not-an-entity";

        Assert.Empty(watch.Items);
        Assert.True(watch.HasNoMatches);
        Assert.Contains(nameof(GlobalWatchViewModel.HasNoMatches), announced);
    }

    [Fact]
    public void Search_typed_while_saving_applies_once_the_save_ends()
    {
        var tree = new Tree();
        var pending = new TaskCompletionSource<bool>();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => pending.Task);
        _ = watch.SetGlobalAsync(deadLetter: false, enabled: true);
        IReadOnlyList<WatchEntityItem> shown = watch.Items;

        watch.SearchText = "billing";

        Assert.Same(shown, watch.Items);
        pending.SetResult(true);
        Assert.Single(watch.Items);
    }

    [Fact]
    public void A_refresh_with_the_same_entities_and_rules_changes_nothing()
    {
        var tree = new Tree();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => Task.FromResult(true));
        IReadOnlyList<WatchEntityItem> shown = watch.Items;
        var announced = new List<string?>();
        watch.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        watch.Refresh(tree.Roots, tree.Rules());

        Assert.Same(shown, watch.Items);
        Assert.Empty(announced);
    }

    [Fact]
    public void A_refresh_with_new_rules_or_entities_shows_them_and_later_edits_keep_them()
    {
        var tree = new Tree();
        var saved = new List<WatchRuleEditor>();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), rules => { saved.Add(rules); return Task.FromResult(true); });
        WatchRuleEditor external = tree.Rules();
        external.SetIncluded(tree.Queue, false);
        EntityNode discovered = Tree.Entity(EntityKind.Queue, "newly-discovered");
        tree.Queues.Children.Add(discovered);

        watch.Refresh(tree.Roots, external);

        Assert.Equal([false, true], watch.Items[0].Children.Select(item => item.IsIncluded));
        _ = watch.SetGlobalAsync(deadLetter: true, enabled: true);
        Assert.Equal(false, Assert.Single(saved).GetIncludedState(tree.Queue));
    }

    [Fact]
    public void A_failed_save_after_a_refresh_puts_back_the_refreshed_rules()
    {
        var tree = new Tree();
        var watch = new GlobalWatchViewModel(tree.Roots, tree.Rules(), _ => Task.FromResult(false));
        WatchRuleEditor external = tree.Rules();
        external.SetGlobal(deadLetter: true, enabled: true);
        watch.Refresh(tree.Roots, external);

        _ = watch.SetGlobalAsync(deadLetter: true, enabled: false);

        Assert.True(watch.GlobalDeadLetter);
    }

    private sealed class Tree
    {
        public Tree()
        {
            Queues.Children.Add(Queue);
            Topic.Children.Add(Billing);
            Topic.Children.Add(Audit);
            Topics.Children.Add(Topic);
        }

        public EntityNode Queues { get; } = new("Queues", "QueueGroup");

        public EntityNode Topics { get; } = new("Topics", "TopicGroup");

        public EntityNode Queue { get; } = Entity(EntityKind.Queue, "checkout-requests");

        public EntityNode Topic { get; } = Entity(EntityKind.Topic, "order-events");

        public EntityNode Billing { get; } = Entity(EntityKind.Subscription, "billing", "order-events");

        public EntityNode Audit { get; } = Entity(EntityKind.Subscription, "audit", "order-events");

        public EntityNode[] Roots => [Queues, Topics];

        public WatchRuleEditor Rules() => new(Roots, []);

        public static EntityNode Entity(EntityKind kind, string name, string? topic = null) => new(name, kind.ToString(),
            new EntityObservation(new DiscoveredEntity(kind, name, topic,
                new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
                new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known))));
    }
}
