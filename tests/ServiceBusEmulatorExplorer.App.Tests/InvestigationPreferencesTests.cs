using System.IO;
using System.Text.Json;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationPreferencesTests
{
    [Fact]
    public async Task Save_and_load_round_trip_protects_connection_strings_and_preserves_approved_preferences()
    {
        string path = CreatePath();
        var preferences = new WorkspacePreferences
        {
            Profiles =
            [
                new InvestigationProfile(
                    "local-emulator",
                    ConnectionProfileDefaults.LocalEmulator,
                    "#007F80",
                    "Use this profile only for local development."),
                new InvestigationProfile(
                    "azure-cli",
                    new ConnectionProfile("Azure", "ignored-runtime", "ignored-admin",
                        ConnectionAuthenticationMode.AzureCli, "orders.servicebus.windows.net"),
                    "#7540BF")
            ],
            SelectedProfileId = "azure-cli",
            CloseToTray = false,
            NotificationsEnabled = false,
            AutoConnectOnSwitch = true,
            WasConnected = true,
            LogExpanded = false,
            TimestampDisplay = TimestampDisplay.Local,
            QueuePageSize = 100,
            TopicPageSize = 200,
            SubscriptionPageSize = 25,
            SearchTimeBudgetSeconds = 120,
            SearchDeliveryBudget = 100_000,
            AutoRefreshSeconds = 15,
            SelectedEntityPath = "orders/billing",
            DeadLetter = true,
            WindowWidth = 1200,
            WindowHeight = 800,
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>
            {
                ["local-emulator"] = [new WatchPreference("orders", true, false)]
            }
        };

        var store = new ProtectedWorkspacePreferencesStore(path);
        await store.SaveAsync(preferences, CancellationToken.None);
        string onDisk = await File.ReadAllTextAsync(path);

        Assert.DoesNotContain(ConnectionProfileDefaults.LocalEmulator.RuntimeConnectionString, onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain(ConnectionProfileDefaults.LocalEmulator.AdministrationConnectionString, onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored-runtime", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored-admin", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("logs", onDisk, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("draft", onDisk, StringComparison.OrdinalIgnoreCase);

        PreferencesLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(preferences.SelectedProfileId, loaded.Preferences.SelectedProfileId);
        Assert.Equal(preferences.QueuePageSize, loaded.Preferences.QueuePageSize);
        Assert.Equal(preferences.TopicPageSize, loaded.Preferences.TopicPageSize);
        Assert.Equal(preferences.SubscriptionPageSize, loaded.Preferences.SubscriptionPageSize);
        Assert.Equal(preferences.SearchTimeBudgetSeconds, loaded.Preferences.SearchTimeBudgetSeconds);
        Assert.Equal(preferences.SearchDeliveryBudget, loaded.Preferences.SearchDeliveryBudget);
        Assert.Equal(preferences.TimestampDisplay, loaded.Preferences.TimestampDisplay);
        Assert.Equal(preferences.Profiles[0], loaded.Preferences.Profiles[0]);
        Assert.Equal(preferences.Profiles[1].Id, loaded.Preferences.Profiles[1].Id);
        Assert.Equal(preferences.Profiles[1].Connection.Name, loaded.Preferences.Profiles[1].Connection.Name);
        Assert.Equal(preferences.Profiles[1].Connection.AuthenticationMode, loaded.Preferences.Profiles[1].Connection.AuthenticationMode);
        Assert.Empty(loaded.Preferences.Profiles[1].Connection.RuntimeConnectionString);
        Assert.Empty(loaded.Preferences.Profiles[1].Connection.AdministrationConnectionString);
        Assert.Equal(preferences.Profiles[1].Connection.FullyQualifiedNamespace,
            loaded.Preferences.Profiles[1].Connection.FullyQualifiedNamespace);
        Assert.Equal(preferences.Watches["local-emulator"], loaded.Preferences.Watches["local-emulator"]);
    }

    [Fact]
    public async Task Load_legacy_profile_array_migrates_in_place_and_retains_azure_cli_profiles()
    {
        string path = CreatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string runtime = "Endpoint=sb://legacy-runtime;SharedAccessKey=legacy-runtime-secret;";
        const string administration = "Endpoint=sb://legacy-admin;SharedAccessKey=legacy-admin-secret;";
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new object[]
        {
            new { name = "Legacy", runtimeConnectionString = runtime, administrationConnectionString = administration },
            new
            {
                name = "Azure", authenticationMode = ConnectionAuthenticationMode.AzureCli,
                fullyQualifiedNamespace = "orders.servicebus.windows.net"
            }
        }));

        PreferencesLoadResult loaded = await new ProtectedWorkspacePreferencesStore(path).LoadAsync(CancellationToken.None);
        string migrated = await File.ReadAllTextAsync(path);

        Assert.Null(loaded.Warning);
        Assert.Equal(2, loaded.Preferences.Profiles.Count);
        Assert.Equal(ConnectionAuthenticationMode.ConnectionString, loaded.Preferences.Profiles[0].Connection.AuthenticationMode);
        Assert.Equal(runtime, loaded.Preferences.Profiles[0].Connection.RuntimeConnectionString);
        Assert.Equal(ConnectionAuthenticationMode.AzureCli, loaded.Preferences.Profiles[1].Connection.AuthenticationMode);
        Assert.Equal("orders.servicebus.windows.net", loaded.Preferences.Profiles[1].Connection.FullyQualifiedNamespace);
        Assert.StartsWith("{", migrated.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain(runtime, migrated, StringComparison.Ordinal);
        Assert.DoesNotContain(administration, migrated, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeConnectionString", migrated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrupt_file_returns_defaults_with_a_sanitized_warning()
    {
        string path = CreatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ definitely not valid preferences }");

        PreferencesLoadResult loaded = await new ProtectedWorkspacePreferencesStore(path).LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded.Warning);
        Assert.Contains("Defaults", loaded.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonException", loaded.Warning, StringComparison.Ordinal);
        Assert.Single(loaded.Preferences.Profiles);
        Assert.Equal("local-emulator", loaded.Preferences.SelectedProfileId);
    }

    [Fact]
    public async Task Canceled_save_leaves_the_previous_valid_file_unchanged()
    {
        string path = CreatePath();
        var store = new ProtectedWorkspacePreferencesStore(path);
        await store.SaveAsync(new WorkspacePreferences(), CancellationToken.None);
        byte[] before = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(new WorkspacePreferences
        {
            QueuePageSize = 200
        }, cancellation.Token));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Invalid_page_timestamp_and_selection_values_are_defaulted_without_duplicate_profiles()
    {
        string path = CreatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var validProfile = new
        {
            id = "profile-one", name = "One", colorHex = "#0069FA", authenticationMode = 1,
            runtimeConnection = (string?)null, administrationConnection = (string?)null,
            fullyQualifiedNamespace = "orders.servicebus.windows.net", warningMessage = ""
        };
        var envelope = new
        {
            version = 1,
            profiles = new[] { validProfile },
            settings = new
            {
                selectedProfileId = "missing",
                timestampDisplay = 99,
                queuePageSize = 10,
                topicPageSize = 999,
                subscriptionPageSize = 0,
                searchTimeBudgetSeconds = 11,
                searchDeliveryBudget = 1,
                watches = new Dictionary<string, object>()
            }
        };
        // The profile has no protected values because Azure CLI credentials are not persisted.
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(envelope));

        PreferencesLoadResult loaded = await new ProtectedWorkspacePreferencesStore(path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal("profile-one", loaded.Preferences.SelectedProfileId);
        Assert.Equal(TimestampDisplay.Utc, loaded.Preferences.TimestampDisplay);
        Assert.Equal(50, loaded.Preferences.QueuePageSize);
        Assert.Equal(50, loaded.Preferences.TopicPageSize);
        Assert.Equal(50, loaded.Preferences.SubscriptionPageSize);
        Assert.Equal(30, loaded.Preferences.SearchTimeBudgetSeconds);
        Assert.Equal(10_000, loaded.Preferences.SearchDeliveryBudget);
    }

    [Fact]
    public async Task Older_envelopes_without_search_budget_fields_use_the_new_defaults()
    {
        string path = CreatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {
              "version": 1,
              "profiles": [{
                "id": "profile-one",
                "name": "One",
                "colorHex": "#0069FA",
                "authenticationMode": 1,
                "fullyQualifiedNamespace": "orders.servicebus.windows.net",
                "warningMessage": ""
              }],
              "settings": {"selectedProfileId": "profile-one", "watches": {}}
            }
            """);

        PreferencesLoadResult loaded = await new ProtectedWorkspacePreferencesStore(path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(30, loaded.Preferences.SearchTimeBudgetSeconds);
        Assert.Equal(10_000, loaded.Preferences.SearchDeliveryBudget);
    }

    private static string CreatePath() => Path.Combine(
        Path.GetTempPath(), "sbe-preferences-tests", Guid.NewGuid().ToString("N"), "connection-profiles.json");
}
