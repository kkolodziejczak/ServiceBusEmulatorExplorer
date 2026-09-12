using System.IO;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationWatchPersistenceTests
{
    private static readonly WatchPreference[] Original = [new("connection:*", false, true)];
    private static readonly WatchPreference[] Updated = [new("connection:*", true, true), new("queue:orders", null, null, false)];
    private static readonly WatchPreference[] OtherProfile = [new("queue:other", true, false)];

    [Fact]
    public async Task Saving_watch_rules_changes_only_the_requested_saved_profile()
    {
        var store = new Store(Initial());
        await using var workspace = Workspace(store);
        await workspace.InitializeAsync();

        await workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, Updated);

        Assert.Equal(Updated, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
        Assert.Equal(OtherProfile, workspace.Preferences.Watches["other"]);
        var saved = Assert.Single(store.Saves);
        Assert.Equal(Updated, saved.Watches[workspace.SelectedProfile.Id]);
        Assert.Equal(OtherProfile, saved.Watches["other"]);
        Assert.Equal(Original, store.Initial.Watches[workspace.SelectedProfile.Id]);
    }

    [Fact]
    public async Task Failed_save_does_not_publish_watch_rules_and_a_retry_can_succeed()
    {
        var store = new Store(Initial()) { FailNextSave = true };
        await using var workspace = Workspace(store);
        await workspace.InitializeAsync();
        int preferenceChanges = 0;
        workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(workspace.Preferences)) preferenceChanges++;
        };

        await Assert.ThrowsAsync<IOException>(() => workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, Updated));

        Assert.Equal(Original, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
        Assert.Empty(store.Saves);
        Assert.Equal(0, preferenceChanges);
        await workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, Updated);
        Assert.Equal(Updated, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
        Assert.Single(store.Saves);
        Assert.Equal(1, preferenceChanges);
    }

    [Fact]
    public async Task Update_for_another_saved_profile_is_rejected_without_saving()
    {
        var store = new Store(Initial());
        await using var workspace = Workspace(store);
        await workspace.InitializeAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => workspace.UpdateWatchRulesAsync("other", Updated));

        Assert.Empty(store.Saves);
        Assert.Equal(OtherProfile, workspace.Preferences.Watches["other"]);
        Assert.Equal(Original, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_snapshot_preserves_watch_changes_committed_after_the_snapshot_was_taken(bool holdWatchSave)
    {
        var store = new Store(Initial());
        await using var workspace = Workspace(store);
        await workspace.InitializeAsync();
        var staleSettings = workspace.Preferences with { QueuePageSize = 100, NotificationsEnabled = false };
        SavePause? pause = holdWatchSave ? store.PauseNextSave() : null;
        Task watchSave = workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, Updated);
        if (pause is not null) await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        else await watchSave;

        Task settingsSave = workspace.ApplyPreferencesAsync(staleSettings);
        try
        {
            if (pause is not null)
            {
                Assert.False(settingsSave.IsCompleted);
                Assert.Empty(store.Saves);
                Assert.Equal(Original, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
            }
        }
        finally { pause?.Release.TrySetResult(); }
        await Task.WhenAll(watchSave, settingsSave).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(Updated, store.Saves[0].Watches[workspace.SelectedProfile.Id]);
        Assert.Equal(Updated, store.Saves[1].Watches[workspace.SelectedProfile.Id]);
        Assert.Equal(OtherProfile, store.Saves[1].Watches["other"]);
        Assert.Equal(100, store.Saves[1].QueuePageSize);
        Assert.False(store.Saves[1].NotificationsEnabled);
        Assert.Equal(Updated, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
        Assert.Equal(100, workspace.Preferences.QueuePageSize);
        Assert.False(workspace.Preferences.NotificationsEnabled);
    }

    private static WorkspacePreferences Initial()
    {
        var defaults = new WorkspacePreferences();
        return defaults with
        {
            Profiles = [defaults.Profiles[0], defaults.Profiles[0] with { Id = "other" }],
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>
            {
                [defaults.SelectedProfileId] = Original,
                ["other"] = OtherProfile
            }
        };
    }

    private static InvestigationWorkspace Workspace(Store store) => new(store,
        new BrokerConnectionWorkflow(
            () => throw new InvalidOperationException("These persistence tests must not connect."),
            _ => throw new InvalidOperationException("These persistence tests must not discover."),
            _ => throw new InvalidOperationException("These persistence tests must not inspect messages.")));

    private sealed class Store(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        private SavePause? nextPause;
        public WorkspacePreferences Initial { get; } = initial;
        public List<WorkspacePreferences> Saves { get; } = [];
        public bool FailNextSave { get; set; }
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(Initial));

        public SavePause PauseNextSave() => nextPause = new();

        public async Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            var pause = nextPause;
            nextPause = null;
            if (pause is not null)
            {
                pause.Entered.TrySetResult();
                await pause.Release.Task.WaitAsync(cancellationToken);
            }
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Simulated preferences persistence failure.");
            }
            Saves.Add(preferences);
        }
    }

    private sealed class SavePause
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
