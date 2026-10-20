using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class NamespaceScopeViewModelTests
{
    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new NamespaceScopeViewModel(null!)).ParamName);

    [Fact]
    public async Task A_queue_shows_one_line_named_after_the_queue()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var scope = new NamespaceScopeViewModel(fixture.Workspace);

        Assert.Equal(new ScopeHeaderState(true, "Queue: orders", null, fixture.Queue, false, false), scope.HeaderFor(libraryVisible: false));
    }

    [Fact]
    public async Task A_subscription_shows_its_topic_and_indents_the_subscription()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var scope = new NamespaceScopeViewModel(fixture.Workspace);

        await fixture.Workspace.Browse.SelectAsync(fixture.Subscription, deadLetter: false);

        Assert.Equal(new ScopeHeaderState(true, "Topic: order-events; Subscription: billing", fixture.Topic, fixture.Subscription, false, true),
            scope.HeaderFor(false));
    }

    [Fact]
    public async Task A_topic_shows_that_it_covers_all_subscriptions()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var scope = new NamespaceScopeViewModel(fixture.Workspace);

        await fixture.Workspace.Browse.SelectAsync(fixture.Topic, deadLetter: false);

        Assert.Equal(new ScopeHeaderState(true, "Topic: order-events; All subscriptions", fixture.Topic, null, true, false), scope.HeaderFor(false));
    }

    [Fact]
    public async Task Searching_or_the_message_library_shows_the_breadcrumb_instead()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var scope = new NamespaceScopeViewModel(fixture.Workspace);

        Assert.Equal(new ScopeHeaderState(false, fixture.Workspace.Surface.EntityPath, null, null, false, false), scope.HeaderFor(libraryVisible: true));
        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        Assert.Equal(new ScopeHeaderState(false, fixture.Workspace.Surface.EntityPath, null, null, false, false), scope.HeaderFor(false));
    }

    [Fact]
    public async Task While_searching_the_tree_selects_the_entity_last_chosen_there()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var scope = new NamespaceScopeViewModel(fixture.Workspace);
        var queue = fixture.Queue.Address!;
        var subscription = fixture.Subscription.Address!;

        Assert.Equal(queue, scope.SelectionToRestore);
        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        Assert.Equal(queue, scope.SelectionToRestore);

        scope.Remember(subscription);
        Assert.Equal(subscription, scope.SelectionToRestore);
        scope.Restored(queue);
        Assert.Equal(subscription, scope.RememberedSelection);

        fixture.Workspace.Search.Clear();
        Assert.Equal(queue, scope.SelectionToRestore);
        scope.Restored(queue);
        Assert.Equal(queue, scope.RememberedSelection);
    }

    [Fact]
    public async Task The_path_to_an_entity_runs_from_its_group_through_its_topic()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var roots = fixture.Workspace.Browse.Roots;

        var path = NamespaceScopeViewModel.PathTo(roots, fixture.Subscription.Address!)!;

        Assert.Equal(3, path.Count);
        Assert.True(path[0].IsGroup);
        Assert.Same(fixture.Topic, path[1]);
        Assert.Same(fixture.Subscription, path[2]);
        Assert.Null(NamespaceScopeViewModel.PathTo(roots, new EntityAddress(EntityKind.Queue, "missing")));
    }
}
