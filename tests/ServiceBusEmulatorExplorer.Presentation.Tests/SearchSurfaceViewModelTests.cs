using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class SearchSurfaceViewModelTests
{
    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new SearchSurfaceViewModel(null!)).ParamName);

    [Fact]
    public async Task Browsing_shows_no_search_and_offers_load_more()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        var state = surface.StateFor(wide: true, searchText: "", libraryVisible: false);

        Assert.False(state.IsSearching);
        Assert.False(state.ShowsSummary);
        Assert.False(state.CanStop);
        Assert.Null(state.NamespaceToolTip);
        Assert.Equal("Load more", state.LoadMoreLabel);
        Assert.Null(state.EmptyList);
        Assert.False(state.CanClear);
        Assert.True(surface.StateFor(true, "orders", false).CanClear);
        Assert.False(surface.NamespaceEmptyFor("", libraryVisible: false).IsShown);
    }

    [Fact]
    public async Task Find_related_applies_only_to_a_focused_message_with_a_correlation_id()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        fixture.Focus(1);
        Assert.False(surface.StateFor(true, "", false).CanFindRelated);
        fixture.Focus(0);
        Assert.True(surface.StateFor(true, "", false).CanFindRelated);
    }

    [Fact]
    public async Task A_finished_search_counts_its_matches_on_wide_windows_and_explains_entity_counts()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        var state = surface.StateFor(wide: true, searchText: "", libraryVisible: false);

        Assert.True(state.IsSearching);
        Assert.True(state.IsComplete);
        Assert.False(state.CanStop);
        Assert.True(state.ShowsSummary);
        Assert.False(state.IsReplaySearch);
        Assert.Equal("checkout* · 3 matches", state.Summary);
        Assert.Equal("checkout*", state.QueryText);
        Assert.Equal(fixture.Workspace.Search.Status, state.Status);
        Assert.Equal("Counts show matching messages found so far. Clear search to restore entity totals.", state.NamespaceToolTip);
        Assert.Equal("Continue", state.LoadMoreLabel);
        Assert.Equal(new SurfaceEmptyState("No matching messages", fixture.Workspace.Search.Status, true, false), state.EmptyList);
        Assert.True(state.CanClear);
        Assert.False(surface.StateFor(wide: false, "", false).ShowsSummary);
        Assert.Null(surface.StateFor(true, "", libraryVisible: true).NamespaceToolTip);
    }

    [Fact]
    public async Task An_invalid_search_says_so_and_how_to_write_one()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        await fixture.Workspace.Search.StartAsync("message:\"unterminated");

        var empty = surface.StateFor(true, "", false).EmptyList!;
        Assert.Equal("Invalid search", empty.Message);
        Assert.Equal(fixture.Workspace.Search.QueryError + "\nUse OR between IDs and * to match any characters.", empty.Description);
    }

    [Fact]
    public async Task A_search_for_replay_copies_summarises_deliveries_even_on_narrow_windows()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        string query = surface.QueryForReplays(["checkout-original-message-1", "checkout-original-message-2", "checkout-original-message-1"]);
        await fixture.Workspace.Search.StartAsync(query, defaultMessageId: true);
        var state = surface.StateFor(wide: false, "", false);

        Assert.Equal("message:\"checkout-original-message-1\" OR message:\"checkout-original-message-2\"", query);
        Assert.Equal(query, surface.ReplayQuery);
        Assert.True(state.IsReplaySearch);
        Assert.True(state.ShowsSummary);
        Assert.Equal("2 replay IDs · 2 deliveries across 1 queue", state.Summary);
        Assert.Equal("2 replay IDs · 2 deliveries across 1 queue\n" + state.Status, state.StatusWithSummary);

        string original = surface.QueryForOriginal("checkout \"original\"");

        Assert.Equal("message:\"checkout \\\"original\\\"\"", original);
        Assert.Null(surface.ReplayQuery);
        Assert.False(surface.StateFor(false, "", false).IsReplaySearch);

        surface.QueryForReplays(["checkout-original-message-1"]);
        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);

        Assert.False(surface.StateFor(false, "", false).IsReplaySearch);
    }

    [Fact]
    public async Task A_running_search_can_be_stopped_and_counts_what_it_found_so_far()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);
        var peek = fixture.Gate();
        fixture.Peeks.BeforePeek = () => peek.Task;

        Task searching = fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        var state = surface.StateFor(true, "", false);

        Assert.False(state.IsComplete);
        Assert.True(state.CanStop);
        Assert.Equal("checkout* · 0 matches so far", state.Summary);
        Assert.Equal("Searching messages…", state.EmptyList!.Message);
        Assert.Equal(new NamespaceEmptyState(true, "Searching for matching entities…"), surface.NamespaceEmptyFor("", false));
        fixture.Workspace.Search.Stop();
        await searching.WaitAsync(TimeSpan.FromSeconds(10));
        var stopped = surface.StateFor(true, "", false);

        Assert.False(stopped.CanStop);
        Assert.False(stopped.IsComplete);
        Assert.Equal("checkout* · 0 matches so far", stopped.Summary);
        Assert.Equal("No matches in the scanned deliveries", stopped.EmptyList!.Message);
        Assert.False(peek.Task.IsCompleted);
    }

    [Fact]
    public async Task An_empty_browse_view_replaces_the_list_with_why_it_is_empty()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        await fixture.Workspace.DisconnectAsync();

        Assert.Equal(new SurfaceEmptyState("Connect to browse messages", "", false, true), surface.StateFor(true, "", false).EmptyList);
    }

    [Fact]
    public async Task The_namespace_list_explains_why_no_entity_is_listed()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var surface = new SearchSurfaceViewModel(fixture.Workspace);

        fixture.Workspace.Browse.FilterEntities("no-such-entity");

        Assert.Equal(new NamespaceEmptyState(true, "No matching entities. Clear the search to show all entities."),
            surface.NamespaceEmptyFor("no-such-entity", libraryVisible: false));
        Assert.False(surface.NamespaceEmptyFor("no-such-entity", libraryVisible: true).IsShown);
        Assert.Equal(new NamespaceEmptyState(true, "No entities found on this connection."), surface.NamespaceEmptyFor("", false));

        fixture.Workspace.Browse.FilterEntities("");
        await fixture.Workspace.Search.StartAsync("no-such-message", defaultMessageId: true);

        Assert.Equal(new NamespaceEmptyState(true,
            "No matching entities found in the scanned deliveries. Clear the search to show all entities."), surface.NamespaceEmptyFor("", false));
    }
}
