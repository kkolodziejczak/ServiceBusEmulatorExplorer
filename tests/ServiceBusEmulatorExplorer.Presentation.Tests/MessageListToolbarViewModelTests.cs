using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class MessageListToolbarViewModelTests
{
    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new MessageListToolbarViewModel(null!)).ParamName);

    [Fact]
    public async Task The_check_all_box_shows_none_some_or_all_and_toggles_them()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);

        Assert.True(toolbar.CanCheckAll);
        Assert.False(toolbar.AllChecked);
        fixture.Rows[0].IsSelected = true;
        Assert.Null(toolbar.AllChecked);

        toolbar.ToggleAll();
        Assert.True(toolbar.AllChecked);
        toolbar.ToggleAll();
        Assert.False(toolbar.AllChecked);

        await fixture.Workspace.Browse.SelectAsync(fixture.Topic, deadLetter: false);
        Assert.False(toolbar.CanCheckAll);
    }

    [Fact]
    public async Task The_bucket_tabs_browse_the_selected_entity_but_not_while_searching()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);

        Assert.True(await toolbar.SelectBucketAsync(deadLetter: false));
        Assert.False(fixture.Workspace.Surface.IsDeadLetter);

        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        Assert.False(await toolbar.SelectBucketAsync(deadLetter: true));
        Assert.False(fixture.Workspace.Browse.IsDeadLetter);
    }

    [Fact]
    public async Task Refresh_and_load_more_read_the_browsed_messages_or_the_search()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);
        int peeks = fixture.Peeks.Count["orders"];

        await toolbar.RefreshAsync();
        Assert.True(fixture.Peeks.Count["orders"] > peeks);

        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        long revision = fixture.Workspace.Search.Revision;
        await toolbar.RefreshAsync();
        Assert.True(fixture.Workspace.Search.Revision > revision);

    }

    [Fact]
    public async Task Load_more_continues_a_stopped_search()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);
        var held = fixture.Gate();
        fixture.Peeks.BeforePeek = () => held.Task;
        Task searching = fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        fixture.Workspace.Search.Stop();
        await searching.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Peeks.BeforePeek = () => Task.CompletedTask;
        Assert.False(fixture.Workspace.Search.IsComplete);

        await toolbar.LoadMoreAsync();

        Assert.True(fixture.Workspace.Search.IsComplete);
        Assert.Equal(3, fixture.Workspace.Search.Messages.Count);
    }

    [Fact]
    public async Task The_interval_is_one_of_the_offered_choices_and_is_saved()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);

        await toolbar.SetIntervalAsync(2);

        Assert.Equal(10, fixture.Store.Saved.AutoRefreshSeconds);
        Assert.Equal(2, toolbar.IntervalIndex);
        await toolbar.SetIntervalAsync(0);
        Assert.Equal(0, toolbar.IntervalIndex);
    }

    [Fact]
    public async Task Pausing_stops_automatic_refresh_and_says_how_to_resume()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);
        await toolbar.SetIntervalAsync(1);
        Assert.Equal(TimeSpan.FromSeconds(5), toolbar.RefreshInterval(libraryVisible: false));
        Assert.Equal("Pause automatic refresh", toolbar.PauseLabel);

        toolbar.TogglePause();

        Assert.True(toolbar.IsPaused);
        Assert.Equal("Resume automatic refresh", toolbar.PauseLabel);
        Assert.Null(toolbar.RefreshInterval(false));
        toolbar.TogglePause();
        Assert.NotNull(toolbar.RefreshInterval(false));
    }

    [Fact]
    public async Task Automatic_refresh_stops_when_off_disconnected_or_while_an_unwatched_search_shows()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);
        await toolbar.SetIntervalAsync(0);
        Assert.Null(toolbar.RefreshInterval(false));
        await toolbar.SetIntervalAsync(3);

        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        Assert.Null(toolbar.RefreshInterval(libraryVisible: false));
        Assert.Equal(TimeSpan.FromSeconds(30), toolbar.RefreshInterval(libraryVisible: true));

        var rules = new WatchRuleEditor(fixture.Workspace.Browse.Roots, []);
        rules.SetScope(fixture.Queue, deadLetter: true, enabled: true);
        await fixture.Workspace.UpdateWatchRulesAsync("profile", rules.Rules);
        Assert.Equal(TimeSpan.FromSeconds(30), toolbar.RefreshInterval(libraryVisible: false));

        await fixture.Workspace.DisconnectAsync();
        Assert.Null(toolbar.RefreshInterval(true));
    }

    [Fact]
    public async Task A_tick_refreshes_the_browsed_messages_but_not_an_investigation_search()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var toolbar = new MessageListToolbarViewModel(fixture.Workspace);
        int peeks = fixture.Peeks.Count["orders"];

        await toolbar.TickAsync(libraryVisible: false);
        Assert.True(fixture.Peeks.Count["orders"] > peeks);

        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        peeks = fixture.Peeks.Count["orders"];
        await toolbar.TickAsync(libraryVisible: false);
        Assert.Equal(peeks, fixture.Peeks.Count["orders"]);
        await toolbar.TickAsync(libraryVisible: true);
        Assert.True(fixture.Peeks.Count["orders"] > peeks);
    }
}
