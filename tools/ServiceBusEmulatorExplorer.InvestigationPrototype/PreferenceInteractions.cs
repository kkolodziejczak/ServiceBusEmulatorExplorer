using System.Windows;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private PrototypePreferencesStore? preferencesStore;
    private PrototypePreferences preferences = new();
    private bool preferencesReady;
    private readonly DispatcherTimer preferencesTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void InitializePreferences(string? path = null)
    {
        preferencesStore = new PrototypePreferencesStore(path);
        if (!preferencesStore.TryLoad(out preferences, out var warning))
        {
            AddLog(warning ?? "Could not load saved preferences.", true);
            return;
        }
        if (preferences.Profiles.Count > 0)
        {
            connectionSettings.Profiles.Clear();
            connectionSettings.Profiles.AddRange(preferences.Profiles);
            connectionSettings.SelectedProfile = connectionSettings.Profiles.FirstOrDefault(profile => profile.Id == preferences.SelectedProfileId)
                ?? connectionSettings.Profiles[0];
            activeConnection = connectionSettings.SelectedProfile;
            RefreshConnectionPresentation();
        }
        preferencesTimer.Tick += (_, _) => SavePreferences();
        Loaded += (_, _) => RestorePreferences();
        Closing += (_, _) => SavePreferences();
        Closed += (_, _) => preferencesTimer.Stop();
    }

    private void RestorePreferences()
    {
        closeToTray = preferences.CloseToTray && (tray is not null || proofLifetime);
        notificationsEnabled = preferences.NotificationsEnabled;
        connectionSettings.AutoConnectOnSwitch = preferences.AutoConnectOnSwitch;
        Width = Math.Clamp(preferences.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(preferences.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        AutoInterval.SelectedIndex = Math.Clamp(preferences.RefreshIndex, 0, 3);
        if (paused != preferences.RefreshPaused) Pause_Click(this, new RoutedEventArgs());
        TimeDisplaySelector.SelectedIndex = Math.Clamp(preferences.TimeIndex, 0, 2);
        if ((LogPanel.Visibility == Visibility.Visible) != preferences.LogExpanded) ToggleLog_Click(this, new RoutedEventArgs());
        var entity = Workspace.Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => !node.IsGroup && node.Path == preferences.SelectedEntityPath);
        RestoreProfileWatch();
        if (preferences.Connected && !string.IsNullOrWhiteSpace(connectionSettings.SelectedProfile.WarningMessage))
        {
            if (Workspace.IsConnected) Workspace.ToggleConnection();
            Connection_Click(this, new RoutedEventArgs());
        }
        else if (Workspace.IsConnected != preferences.Connected) Connection_Click(this, new RoutedEventArgs());
        if (entity is not null) ChangeScope(() => Workspace.SelectEntity(entity));
        ChangeMessageView(preferences.DeadLetter);
        SelectInitialEntity();
        if (preferences.AppliedSearch.Length > 0 && Workspace.IsConnected)
        {
            SearchBox.Text = preferences.AppliedSearch;
            BeginGlobalSearch(preferences.SearchByMessageId);
        }
        else SearchBox.Text = preferences.SearchText;
        UpdateWatchSurface();
        preferencesReady = true;
    }

    private void RestoreProfileWatch()
    {
        watchRules.Clear();
        if (activeConnection is not null && preferences.Watches.TryGetValue(activeConnection.Id, out var saved)) watchRules.Restore(saved);
    }

    private void QueuePreferencesSave()
    {
        if (!preferencesReady) return;
        preferencesTimer.Stop();
        preferencesTimer.Start();
    }

    private void SavePreferences()
    {
        preferencesTimer.Stop();
        if (!preferencesReady || preferencesStore is null) return;
        if (activeConnection is not null) preferences.Watches[activeConnection.Id] = watchRules.Capture();
        preferences.Profiles = connectionSettings.Profiles.ToList();
        preferences.SelectedProfileId = connectionSettings.SelectedProfile.Id;
        preferences.CloseToTray = closeToTray;
        preferences.NotificationsEnabled = notificationsEnabled;
        preferences.AutoConnectOnSwitch = connectionSettings.AutoConnectOnSwitch;
        preferences.RefreshIndex = AutoInterval.SelectedIndex;
        preferences.RefreshPaused = paused;
        preferences.SearchByMessageId = Workspace.SearchByMessageId;
        preferences.TimeIndex = TimeDisplaySelector.SelectedIndex;
        preferences.LogExpanded = LogPanel.Visibility == Visibility.Visible;
        preferences.Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
        preferences.Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height;
        preferences.SelectedEntityPath = Workspace.SelectedEntity?.Path ?? "";
        preferences.DeadLetter = Workspace.IsDeadLetter;
        preferences.Connected = Workspace.IsConnected;
        preferences.SearchText = SearchBox.Text;
        preferences.AppliedSearch = Workspace.IsCorrelationSearch ? Workspace.CorrelationQuery : "";
        if (!preferencesStore.TrySave(preferences, out var warning)) AddLog(warning ?? "Could not save preferences.", true);
    }
}
