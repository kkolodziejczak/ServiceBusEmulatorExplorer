using System.IO;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class MessageColumnPreferencesTests
{
    private static readonly string[] CustomizedColumns =
    [
        "default:event",
        "default:correlation",
        "default:enqueued",
        "system:DeliveryCount",
        "application:tenantId"
    ];

    private static readonly WatchPreference[] UpdatedWatches =
    [
        new("connection:*", true, true),
        new("queue:orders", null, null, false)
    ];

    [Fact]
    public async Task Protected_store_reloads_global_columns_and_preserves_existing_saved_settings()
    {
        string path = CreatePath();
        try
        {
            WorkspacePreferences preferences = Initial() with
            {
                MessageColumns = CustomizedColumns,
                MessageColumnOrder = CustomizedColumns.Reverse().Append("application:hidden").ToArray(),
                CloseToTray = false,
                NotificationsEnabled = false,
                SelectedEntityPath = "orders",
                QueuePageSize = 100
            };

            await new ProtectedWorkspacePreferencesStore(path).SaveAsync(preferences, CancellationToken.None);
            PreferencesLoadResult restarted = await new ProtectedWorkspacePreferencesStore(path)
                .LoadAsync(CancellationToken.None);

            Assert.Null(restarted.Warning);
            Assert.Equal(CustomizedColumns, restarted.Preferences.MessageColumns);
            Assert.Equal(preferences.MessageColumnOrder, restarted.Preferences.MessageColumnOrder);
            Assert.False(restarted.Preferences.CloseToTray);
            Assert.False(restarted.Preferences.NotificationsEnabled);
            Assert.Equal("orders", restarted.Preferences.SelectedEntityPath);
            Assert.Equal(100, restarted.Preferences.QueuePageSize);
            Assert.Equal(preferences.Watches.Count, restarted.Preferences.Watches.Count);
            foreach ((string profileId, IReadOnlyList<WatchPreference> rules) in preferences.Watches)
                Assert.Equal(rules, restarted.Preferences.Watches[profileId]);
            Assert.Equal(preferences.ReplayFamilies.Count, restarted.Preferences.ReplayFamilies.Count);
            foreach ((string profileId, IReadOnlyList<ReplayFamilyState> families) in preferences.ReplayFamilies)
                Assert.Equal(families, restarted.Preferences.ReplayFamilies[profileId]);
            Assert.Equal(preferences.ReplayAttempts, restarted.Preferences.ReplayAttempts);

            await new ProtectedWorkspacePreferencesStore(path).SaveAsync(
                preferences with { MessageColumns = null }, CancellationToken.None);
            PreferencesLoadResult legacyDefaults = await new ProtectedWorkspacePreferencesStore(path)
                .LoadAsync(CancellationToken.None);
            Assert.Null(legacyDefaults.Preferences.MessageColumns);
        }
        finally
        {
            DeletePreferenceFile(path);
        }
    }

    [Fact]
    public async Task Column_update_and_stale_settings_snapshot_preserve_current_watch_and_replay_state()
    {
        var store = new MemoryStore(Initial());
        await using var workspace = CreateWorkspace(store);
        await workspace.InitializeAsync();
        WorkspacePreferences staleSettings = workspace.Preferences with
        {
            QueuePageSize = 100,
            NotificationsEnabled = false
        };

        await workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, UpdatedWatches);
        await workspace.UpdateMessageColumnsAsync(CustomizedColumns, CustomizedColumns.Reverse().ToArray());
        await workspace.ApplyPreferencesAsync(staleSettings);

        WorkspacePreferences saved = Assert.Single(store.Saves, preferences => preferences.QueuePageSize == 100);
        Assert.Equal(CustomizedColumns, workspace.Preferences.MessageColumns);
        Assert.Equal(CustomizedColumns, saved.MessageColumns);
        Assert.Equal(CustomizedColumns.Reverse(), saved.MessageColumnOrder);
        Assert.Equal(UpdatedWatches, saved.Watches[workspace.SelectedProfile.Id]);
        Assert.Equal(InitialReplayFamilies()[workspace.SelectedProfile.Id],
            saved.ReplayFamilies[workspace.SelectedProfile.Id]);
        Assert.Equal(InitialReplayAttempts(), saved.ReplayAttempts);
        Assert.False(saved.NotificationsEnabled);
    }

    [Fact]
    public async Task Failed_column_save_does_not_publish_the_new_value_and_can_be_retried()
    {
        var store = new MemoryStore(Initial()) { FailNextSave = true };
        await using var workspace = CreateWorkspace(store);
        await workspace.InitializeAsync();
        int preferenceChanges = 0;
        workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(workspace.Preferences)) preferenceChanges++;
        };

        await Assert.ThrowsAsync<IOException>(() => workspace.UpdateMessageColumnsAsync(CustomizedColumns));

        Assert.Null(workspace.Preferences.MessageColumns);
        Assert.Empty(store.Saves);
        Assert.Equal(0, preferenceChanges);

        await workspace.UpdateMessageColumnsAsync(CustomizedColumns);
        Assert.Equal(CustomizedColumns, workspace.Preferences.MessageColumns);
        Assert.Equal(CustomizedColumns, Assert.Single(store.Saves).MessageColumns);
        Assert.Equal(1, preferenceChanges);
    }

    private static WorkspacePreferences Initial()
    {
        WorkspacePreferences defaults = new();
        return defaults with
        {
            Profiles =
            [
                defaults.Profiles[0],
                defaults.Profiles[0] with { Id = "other" }
            ],
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>
            {
                [defaults.SelectedProfileId] = [new("connection:*", false, true)],
                ["other"] = [new("queue:other", true, false)]
            },
            ReplayFamilies = InitialReplayFamilies(),
            ReplayAttempts = InitialReplayAttempts()
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ReplayFamilyState>> InitialReplayFamilies()
    {
        ReplayFamilyState family = Family();
        return new Dictionary<string, IReadOnlyList<ReplayFamilyState>>
        {
            ["local-emulator"] = [family],
            ["other"] = [family with { RootFingerprint = new string('B', 64) }]
        };
    }

    private static IReadOnlyList<ReplayAttempt> InitialReplayAttempts()
    {
        ReplayFamilyState family = Family();
        return
        [
            new ReplayAttempt(new Guid("44444444-4444-4444-4444-444444444444"), "local-emulator", new string('C', 64),
                new ReplayReservation(family, "replay-id"), family.OriginalSource, 7,
                new string('D', 64), family.OriginalMessageId, DateTimeOffset.UnixEpoch.AddDays(1),
                ReplaySendStatus.Uncertain)
        ];
    }

    private static ReplayFamilyState Family() => new(new Guid("11111111-1111-1111-1111-111111111111"), new string('A', 64), "original-id",
        new EntityAddress(EntityKind.Queue, "orders"), 1);

    private static string CreatePath() => Path.Combine(Path.GetTempPath(), "sbe-message-columns",
        Guid.NewGuid().ToString("N"), "profiles.json");

    private static void DeletePreferenceFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static InvestigationWorkspace CreateWorkspace(MemoryStore store) => new(store,
        new BrokerConnectionWorkflow(
            () => throw new InvalidOperationException("Column preference tests must not connect."),
            _ => throw new InvalidOperationException("Column preference tests must not discover entities."),
            _ => throw new InvalidOperationException("Column preference tests must not inspect messages.")));

    private sealed class MemoryStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public List<WorkspacePreferences> Saves { get; } = [];
        public bool FailNextSave { get; set; }

        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Simulated preferences persistence failure.");
            }

            Saves.Add(preferences);
            return Task.CompletedTask;
        }
    }
}
