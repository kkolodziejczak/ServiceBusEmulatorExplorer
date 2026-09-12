using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public sealed record InvestigationProfile(
    string Id,
    ConnectionProfile Connection,
    string ColorHex = "#0069FA",
    string WarningMessage = "");

public enum TimestampDisplay { Utc, Local }

public sealed record WatchPreference(string ScopeKey, bool? Active, bool? DeadLetter, bool? Included = null);

public sealed record WorkspacePreferences
{
    public IReadOnlyList<InvestigationProfile> Profiles { get; init; } =
        [new("local-emulator", ConnectionProfileDefaults.LocalEmulator)];
    public string SelectedProfileId { get; init; } = "local-emulator";
    public bool CloseToTray { get; init; } = true;
    public bool NotificationsEnabled { get; init; } = true;
    public bool AutoConnectOnSwitch { get; init; }
    public bool WasConnected { get; init; }
    public bool LogExpanded { get; init; } = true;
    public TimestampDisplay TimestampDisplay { get; init; }
    public int QueuePageSize { get; init; } = 50;
    public int TopicPageSize { get; init; } = 50;
    public int SubscriptionPageSize { get; init; } = 50;
    public int SearchTimeBudgetSeconds { get; init; } = 30;
    public int SearchDeliveryBudget { get; init; } = 10_000;
    public int AutoRefreshSeconds { get; init; }
    public string SelectedEntityPath { get; init; } = "";
    public bool DeadLetter { get; init; }
    public double WindowWidth { get; init; } = 1500;
    public double WindowHeight { get; init; } = 1000;
    public IReadOnlyDictionary<string, IReadOnlyList<WatchPreference>> Watches { get; init; } =
        new Dictionary<string, IReadOnlyList<WatchPreference>>();
}

public sealed record PreferencesLoadResult(WorkspacePreferences Preferences, string? Warning = null);

public interface IWorkspacePreferencesStore
{
    Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken);
}
