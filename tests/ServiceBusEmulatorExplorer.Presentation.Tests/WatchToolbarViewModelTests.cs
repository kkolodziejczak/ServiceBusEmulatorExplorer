using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WatchToolbarViewModelTests
{
    private const string SaveFailure = "Watch preferences could not be saved. Previous rules remain active.";

    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new WatchToolbarViewModel(null!)).ParamName);

    [Fact]
    public async Task Nothing_is_watched_at_first_and_the_popup_has_no_entity()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);

        Assert.Equal(new WatchToolbarState(false, true, true, 0, true, false, false, false, false), watch.StateFor(watch.CreateRules()));
        Assert.Null(watch.PopupEntity);
        Assert.Equal("Select a queue, topic or subscription to watch", watch.PopupTitle);
    }

    [Fact]
    public async Task The_popup_edits_the_selected_entity_but_none_while_searching()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        var announced = new List<string?>();
        watch.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        watch.OpenPopup();

        Assert.Same(fixture.Queue, watch.PopupEntity);
        Assert.Equal("Watch orders", watch.PopupTitle);
        Assert.Contains(nameof(watch.PopupTitle), announced);
        Assert.True(watch.StateFor(watch.CreateRules()).CanChoose);
        watch.ClosePopup();
        Assert.Null(watch.PopupEntity);

        await watch.SetScopeAsync(fixture.Queue, deadLetter: false, enabled: true);
        Assert.True(watch.StateFor(watch.CreateRules()).IsWatching);
        await fixture.Workspace.Search.StartAsync("*", defaultMessageId: true);
        watch.OpenPopup();

        Assert.Null(watch.PopupEntity);
        Assert.Equal("Select a queue, topic or subscription to watch", watch.PopupTitle);
        Assert.False(watch.StateFor(watch.CreateRules()).IsWatching);
    }

    [Fact]
    public async Task Watching_the_popup_entity_saves_it_and_shows_it_watched()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        var saving = new List<bool>();
        watch.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(watch.IsSaving)) saving.Add(watch.IsSaving); };
        watch.OpenPopup();

        await watch.SetPopupScopeAsync(deadLetter: true, enabled: true);

        Assert.Equal([true, false], saving);
        Assert.Single(fixture.Store.Saved.Watches["profile"]);
        Assert.Equal(new WatchToolbarState(true, true, true, 1, true, false, true, true, true), watch.StateFor(watch.CreateRules()));
    }

    [Fact]
    public async Task Stop_watching_clears_both_buckets_and_reports_whether_it_ran()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        Assert.False(await watch.StopWatchingAsync());
        watch.OpenPopup();
        await watch.SetPopupScopeAsync(deadLetter: false, enabled: true);
        await watch.SetPopupScopeAsync(deadLetter: true, enabled: true);
        Assert.Equal(1, watch.StateFor(watch.CreateRules()).WatchedCount);

        Assert.True(await watch.StopWatchingAsync());

        var state = watch.StateFor(watch.CreateRules());
        Assert.False(state.IsWatching);
        Assert.Equal(0, state.WatchedCount);
        Assert.False(state.CanStopWatching);
    }

    [Fact]
    public async Task The_popup_changes_nothing_without_an_entity_or_a_connection()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        int saves = 0;
        fixture.Store.BeforeSave = () => { saves++; return Task.CompletedTask; };

        await watch.SetPopupScopeAsync(deadLetter: false, enabled: true);
        watch.OpenPopup();
        await fixture.Workspace.DisconnectAsync();
        saves = 0;
        await watch.SetPopupScopeAsync(deadLetter: false, enabled: true);

        Assert.Equal(0, saves);
        Assert.False(watch.StateFor(watch.CreateRules()).CanWatch);
        Assert.False(watch.StateFor(watch.CreateRules()).CanOpenOverview);
    }

    [Fact]
    public async Task While_saving_the_buttons_and_popup_wait_and_overlapping_saves_are_each_announced()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        var release = fixture.Gate();
        fixture.Store.BeforeSave = () => release.Task;
        var saving = new List<bool>();
        watch.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(watch.IsSaving)) saving.Add(watch.IsSaving); };
        watch.OpenPopup();

        Task first = watch.SetScopeAsync(fixture.Queue, deadLetter: false, enabled: true);
        Task second = watch.SetScopeAsync(fixture.Queue, deadLetter: true, enabled: true);
        var state = watch.StateFor(watch.CreateRules());
        Task<bool> stopping = watch.StopWatchingAsync();
        Task choosing = watch.SetPopupScopeAsync(deadLetter: false, enabled: false);
        Assert.True(stopping.IsCompletedSuccessfully);
        Assert.True(choosing.IsCompletedSuccessfully);
        bool stopped = stopping.Result;
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(new WatchToolbarState(false, false, false, 0, false, false, false, false, false), state);
        Assert.False(stopped);
        Assert.Equal([true, true, false, false], saving);
        Assert.True(watch.StateFor(watch.CreateRules()).IsWatching);
    }

    [Fact]
    public async Task A_failed_save_is_reported_and_keeps_the_previous_rules()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        await watch.SetScopeAsync(fixture.Queue, deadLetter: true, enabled: true);
        fixture.Store.BeforeSave = () => throw new IOException("disk full");
        var rules = watch.CreateRules();
        rules.SetScope(fixture.Queue, deadLetter: true, enabled: false);
        rules.SetScope(fixture.Queue, deadLetter: false, enabled: true);

        Assert.False(await watch.SaveAsync("profile", rules));

        Assert.Contains(SaveFailure, fixture.Log);
        Assert.False(watch.IsSaving);
        var kept = watch.CreateRules();
        Assert.True(kept.IsWatched(fixture.Queue, deadLetter: true));
        Assert.False(kept.IsWatched(fixture.Queue, deadLetter: false));
    }

    [Fact]
    public async Task A_save_for_a_profile_that_is_no_longer_selected_is_dropped_quietly()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var watch = new WatchToolbarViewModel(fixture.Workspace);
        var rules = watch.CreateRules();
        rules.SetScope(fixture.Queue, deadLetter: false, enabled: true);

        Assert.False(await watch.SaveAsync("another-profile", rules));

        Assert.DoesNotContain(SaveFailure, fixture.Log);
        Assert.Equal(0, watch.StateFor(watch.CreateRules()).WatchedCount);
    }
}
