using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class MessageColumnsViewModelTests
{
    private const string Event = MessageColumnCatalog.Event;
    private const string Correlation = MessageColumnCatalog.Correlation;
    private const string Enqueued = MessageColumnCatalog.Enqueued;
    private const string Tenant = "application:tenant";
    private const string MessageId = "system:MessageId";

    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new MessageColumnsViewModel(null!)).ParamName);

    [Fact]
    public async Task By_default_the_three_default_columns_show_and_enqueued_hides_on_narrow_windows()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);

        var layout = columns.Refresh(chooserOpen: false, narrow: false);

        Assert.False(layout.IsFixed);
        Assert.Equal([Event, Correlation, Enqueued], layout.Shown);
        Assert.Equal([Event, Correlation, Enqueued], layout.Order);
        Assert.True(layout.ShowsEvent && layout.ShowsCorrelation && layout.ShowsEnqueued);
        Assert.False(columns.Refresh(false, narrow: true).ShowsEnqueued);
        Assert.Contains(layout.PropertyColumns, column => column.Id == Tenant);
        Assert.DoesNotContain(layout.PropertyColumns, column => column.Section == "Default columns");
        Assert.Equal([Event, Correlation, Enqueued], columns.Options.Take(3).Select(option => option.Id));
        Assert.All(columns.Options.Take(3), option => Assert.True(option.IsSelected && option.CanToggle));
        Assert.Equal([false, true, true], columns.Options.Take(3).Select(option => option.CanMoveUp));
        Assert.Equal([true, true, false], columns.Options.Take(3).Select(option => option.CanMoveDown));
        Assert.Equal("system:BodySize", columns.Options[3].Id);
        Assert.Equal(["application:Attempt", Tenant], columns.Options.TakeLast(2).Select(option => option.Id));
    }

    [Fact]
    public async Task Showing_a_property_column_saves_it_and_announces_the_change_before_and_after_saving()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        var announced = new List<IReadOnlyList<string>>();
        columns.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(columns.Selected)) announced.Add(columns.Selected); };

        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Tenant), true);

        Assert.Equal([Event, Correlation, Enqueued, Tenant], fixture.Store.Saved.MessageColumns);
        Assert.Equal(2, announced.Count);
        Assert.Contains(Tenant, columns.Refresh(false, false).Shown);
    }

    [Fact]
    public async Task The_last_shown_column_cannot_be_hidden()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Correlation), false);
        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Enqueued), false);
        columns.Refresh(false, false);
        var onlyEvent = columns.Options.Single(option => option.Id == Event);
        Assert.False(onlyEvent.CanToggle);
        int saves = 0;
        fixture.Store.BeforeSave = () => { saves++; return Task.CompletedTask; };
        var announced = new List<string?>();
        columns.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        await columns.ToggleAsync(onlyEvent, false);

        Assert.Equal(0, saves);
        Assert.Equal([nameof(columns.Selected)], announced);
        columns.Refresh(false, false);
        Assert.True(onlyEvent.IsSelected);
    }

    [Fact]
    public async Task A_chosen_selection_shows_enqueued_even_on_narrow_windows()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);

        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Correlation), false);

        var layout = columns.Refresh(false, narrow: true);
        Assert.True(layout.ShowsEnqueued);
        Assert.False(layout.ShowsCorrelation);
    }

    [Fact]
    public async Task Moving_a_shown_column_swaps_it_with_its_neighbour_and_refuses_moves_past_the_ends()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);

        Assert.True(await columns.MoveAsync(Correlation, up: true));
        Assert.Equal([Correlation, Event, Enqueued], fixture.Store.Saved.MessageColumnOrder);
        Assert.False(await columns.MoveAsync(Correlation, up: true));
        Assert.False(await columns.MoveAsync(Enqueued, up: false));
        Assert.False(await columns.MoveAsync(MessageId, up: true));
        Assert.True(await columns.MoveAsync(Correlation, up: false));
        Assert.Equal([Event, Correlation, Enqueued], fixture.Store.Saved.MessageColumnOrder);
    }

    [Fact]
    public async Task Dragging_columns_saves_the_shown_ones_in_their_new_order()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);

        await columns.ReorderAsync([null!, "", Enqueued, MessageId, Event, Correlation]);

        Assert.Equal([Enqueued, Event, Correlation], fixture.Store.Saved.MessageColumnOrder);
        Assert.Equal([Enqueued, Event, Correlation], columns.Refresh(false, false).Order);
    }

    [Fact]
    public async Task Reset_brings_back_the_default_columns_and_order()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Tenant), true);
        await columns.MoveAsync(Tenant, up: true);

        await columns.ResetAsync();

        Assert.Equal([Event, Correlation, Enqueued], fixture.Store.Saved.MessageColumns);
        Assert.Equal([Event, Correlation, Enqueued], fixture.Store.Saved.MessageColumnOrder);
    }

    [Fact]
    public async Task While_saving_the_unsaved_choice_shows_and_only_the_latest_save_clears_it()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        var first = fixture.Gate();
        fixture.Store.BeforeSave = () => first.Task;
        int announced = 0;
        columns.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(columns.Selected)) announced++; };

        Task hiding = columns.ToggleAsync(columns.Options.Single(option => option.Id == Enqueued), false);
        Assert.Equal([Event, Correlation], columns.Selected);
        Assert.False(columns.Refresh(false, narrow: false).ShowsEnqueued);
        var second = fixture.Gate();
        fixture.Store.BeforeSave = () => second.Task;
        Task showing = columns.ToggleAsync(columns.Options.Single(option => option.Id == Tenant), true);
        first.SetResult();
        await hiding.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([Event, Correlation, Tenant], columns.Selected);
        Assert.Equal(2, announced);
        second.SetResult();
        await showing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, announced);
        Assert.Equal([Event, Correlation, Tenant], fixture.Store.Saved.MessageColumns);
    }

    [Fact]
    public async Task A_failed_save_puts_back_the_saved_columns_and_says_so()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        fixture.Store.BeforeSave = () => throw new IOException("disk full");

        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Tenant), true);

        Assert.Contains(fixture.Workspace.Activity, entry => entry.Warning
            && entry.Message == "Column choices could not be saved. Restored the last saved selection and order; try again.");
        Assert.Equal([Event, Correlation, Enqueued], columns.Selected);
        Assert.DoesNotContain(Tenant, columns.Refresh(false, false).Shown);
        Assert.False(columns.Options.Single(option => option.Id == Tenant).IsSelected);
    }

    [Fact]
    public async Task Messages_from_several_sources_use_the_fixed_layout()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Event), false);
        await columns.ToggleAsync(columns.Options.Single(option => option.Id == Enqueued), false);

        await fixture.Workspace.Browse.SelectAsync(fixture.Topic, deadLetter: false);
        var layout = columns.Refresh(false, narrow: true);

        Assert.True(columns.IsFixedLayout);
        Assert.True(layout.IsFixed);
        Assert.True(layout.ShowsEvent);
        Assert.False(layout.ShowsCorrelation);
        Assert.False(layout.ShowsEnqueued);
        Assert.Empty(layout.Shown);
        Assert.True(columns.Refresh(false, narrow: false).ShowsEnqueued);
    }

    [Fact]
    public async Task Choices_for_properties_no_longer_listed_are_removed_only_while_the_chooser_is_closed()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var columns = new MessageColumnsViewModel(fixture.Workspace);
        columns.Refresh(false, false);
        await fixture.Workspace.Browse.SelectAsync(fixture.Subscription, deadLetter: false);

        columns.Refresh(chooserOpen: true, narrow: false);
        Assert.Contains(columns.Options, option => option.Id == Tenant);
        columns.Refresh(chooserOpen: false, narrow: false);
        Assert.DoesNotContain(columns.Options, option => option.Id == Tenant);
    }
}
