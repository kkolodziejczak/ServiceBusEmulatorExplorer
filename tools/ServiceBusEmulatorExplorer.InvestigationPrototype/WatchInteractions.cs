using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private readonly WatchRules watchRules = new();
    private IReadOnlyList<(string Path, bool DeadLetter)> watchedLocations => watchRules.EffectiveLocations(Workspace.Roots);
    private GlobalWatchWindow? globalWatchWindow;
    private readonly Dictionary<(string Path, bool DeadLetter), List<MessageRow>> pendingWatchMessages = [];
    private readonly DispatcherTimer watchTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private WatchNotificationWindow? watchNotification;
    private PrototypeTray? tray;
    private bool closeToTray = true;
    private bool notificationsEnabled = true;
    private bool proofLifetime = true;
    private bool exiting;
    private PrototypeSettingsWindow? settingsWindow;
    private readonly PrototypeConnectionSettings connectionSettings = new();
    private string? watchPopupPath;
    private bool updatingWatchChoices;
    private PrototypeConnectionProfile? activeConnection;

    private void InitializeWatch()
    {
        activeConnection = connectionSettings.SelectedProfile;
        watchTimer.Tick += (_, _) => SimulateWatchedArrivals();
        Closing += OnPrototypeClosing;
    }

    public void ConfigureApplicationLifetime(bool proofMode, bool persistPreferences = false)
    {
        proofLifetime = proofMode;
        if (proofMode) return;
        try { tray = new PrototypeTray(RestorePrototype, ExitPrototype); }
        catch (Exception error) when (error is System.IO.IOException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            closeToTray = false;
            AddLog($"System tray unavailable: {error.Message}. Closing will exit.", true);
        }
        if (persistPreferences) InitializePreferences();
    }

    private void RestorePrototype()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitPrototype()
    {
        exiting = true;
        Close();
    }

    private void OnPrototypeClosing(object? sender, CancelEventArgs e)
    {
        if (proofLifetime || exiting || !closeToTray || tray is null) return;
        e.Cancel = true;
        Hide();
    }

    private void CloseWatch()
    {
        watchTimer.Stop();
        watchNotification?.Close(); watchNotification = null;
        tray?.Dispose(); tray = null;
        settingsWindow?.Close(); settingsWindow = null;
        globalWatchWindow?.Close(); globalWatchWindow = null;
    }

    private void Watch_Click(object sender, RoutedEventArgs e)
    {
        var entity = Workspace.SelectedEntity;
        if (entity is null || entity.IsGroup || Workspace.IsCorrelationSearch)
        {
            watchPopupPath = null;
            WatchPopupTitle.Text = "Select a queue, topic or subscription to watch";
        }
        else
        {
            watchPopupPath = entity.Path;
            WatchPopupTitle.Text = $"Watch {entity.Name}";
        }
        WatchActiveChoice.IsEnabled = WatchDlqChoice.IsEnabled = watchPopupPath is not null && Workspace.IsConnected;
        updatingWatchChoices = true;
        WatchActiveChoice.IsChecked = ScopeIsWatched(watchPopupPath, false);
        WatchDlqChoice.IsChecked = ScopeIsWatched(watchPopupPath, true);
        updatingWatchChoices = false;
        StopWatchingButton.IsEnabled = WatchActiveChoice.IsChecked == true || WatchDlqChoice.IsChecked == true;
        WatchPopup.IsOpen = true;
    }

    private void WatchChoice_Click(object sender, RoutedEventArgs e)
    {
        if (updatingWatchChoices || watchPopupPath is null || !Workspace.IsConnected) return;
        var deadLetter = ReferenceEquals(sender, WatchDlqChoice);
        SetWatched(watchPopupPath, deadLetter, ((CheckBox)sender).IsChecked == true);
        StopWatchingButton.IsEnabled = ScopeIsWatched(watchPopupPath, false) || ScopeIsWatched(watchPopupPath, true);
    }

    private void StopWatching_Click(object sender, RoutedEventArgs e)
    {
        if (watchPopupPath is null) return;
        SetWatched(watchPopupPath, false, false);
        SetWatched(watchPopupPath, true, false);
        WatchPopup.IsOpen = false;
    }

    private ContextMenu NewWatchMenu(object sender) => new()
    {
        PlacementTarget = sender as UIElement, Placement = PlacementMode.Bottom,
        Style = (Style)FindResource("WatchOverviewMenuStyle")
    };

    private void AddWatchChoice(ContextMenu menu, string path, string label, bool deadLetter)
    {
        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = watchedLocations.Contains((path, deadLetter)), IsEnabled = Workspace.IsConnected };
        item.Click += (_, _) => SetWatched(path, deadLetter, item.IsChecked);
        menu.Items.Add(item);
    }

    public void SetWatched(string path, bool deadLetter, bool enabled)
    {
        watchRules.SetScope(path, deadLetter, enabled);
        var entity = Workspace.Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => node.Path == path);
        // An explicit topic action applies to its present subscriptions and establishes inheritance for future ones.
        if (entity?.Kind == "Topic")
            foreach (var child in entity.Children) watchRules.SetScope(child.Path, deadLetter, enabled);
        WatchRulesChanged();
        AddLog($"{(enabled ? "Watching" : "Stopped watching")} {path} · {(deadLetter ? "DLQ" : "Active")}{(!watchRules.IsIncluded(path, Workspace.Roots) ? " · Not included in Watch" : "")}.");
    }

    private void WatchRulesChanged()
    {
        UpdateWatchSurface();
        AdvanceWatchNotification();
        QueuePreferencesSave();
    }

    private bool ScopeIsWatched(string? path, bool deadLetter)
    {
        if (path is null) return false;
        var entity = Workspace.Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => node.Path == path);
        return entity?.Kind == "Topic"
            ? entity.Children.Any(child => watchRules.IsWatched(child.Path, deadLetter, Workspace.Roots))
            : watchRules.IsWatched(path, deadLetter, Workspace.Roots);
    }

    private void GlobalWatch_Click(object sender, RoutedEventArgs e)
    {
        WatchPopup.IsOpen = false;
        if (globalWatchWindow is not null) { globalWatchWindow.Activate(); return; }
        globalWatchWindow = new GlobalWatchWindow(Workspace.Roots, watchRules, () =>
        {
            WatchRulesChanged();
            AddLog("Updated global Watch rules and included entities.");
        }) { Owner = this };
        ProfileTheme.Apply(globalWatchWindow, connectionSettings.SelectedProfile.ColorHex);
        globalWatchWindow.Closed += (_, _) => globalWatchWindow = null;
        globalWatchWindow.Show();
    }

    private void UpdateWatchSurface()
    {
        var effective = watchedLocations;
        var removedPending = pendingWatchMessages.Keys.Where(location => !effective.Contains(location)).ToArray();
        foreach (var location in removedPending) pendingWatchMessages.Remove(location);
        if (removedPending.Length > 0) AdvanceWatchNotification();
        if (Workspace.IsConnected && effective.Count > 0 && !proofLifetime) watchTimer.Start(); else watchTimer.Stop();
        if (!Workspace.IsConnected) WatchPopup.IsOpen = false;
        if (WatchPopup.IsOpen)
        {
            updatingWatchChoices = true;
            WatchActiveChoice.IsChecked = ScopeIsWatched(watchPopupPath, false);
            WatchDlqChoice.IsChecked = ScopeIsWatched(watchPopupPath, true);
            updatingWatchChoices = false;
            StopWatchingButton.IsEnabled = WatchActiveChoice.IsChecked == true || WatchDlqChoice.IsChecked == true;
        }
        if (FindName("WatchButtonLabel") is not TextBlock label) return;
        var path = Workspace.SelectedEntity?.Path;
        label.Text = !Workspace.IsCorrelationSearch && (ScopeIsWatched(path, false) || ScopeIsWatched(path, true)) ? "Watching" : "Watch";
        var isWatching = label.Text == "Watching";
        WatchBell.Fill = isWatching ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
        WatchOffSlash.Visibility = isWatching ? Visibility.Collapsed : Visibility.Visible;
        var entityCount = watchedLocations.Select(location => location.Path).Distinct().Count();
        WatchSummaryButton.Visibility = entityCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        WatchCount.Text = entityCount.ToString();
        WatchSummaryButton.ToolTip = $"Overview of {entityCount} watched entities and pending notifications";
        System.Windows.Automation.AutomationProperties.SetName(WatchSummaryButton, $"Overview of {entityCount} watched entities");
    }

    public void SimulateWatchedArrivals()
    {
        if (!Workspace.IsConnected) return;
        UpdateWatchSurface();
        foreach (var location in watchedLocations.ToArray())
        {
            var message = Workspace.InjectSampleArrival(location.Path, location.DeadLetter);
            if (!pendingWatchMessages.TryGetValue(location, out var messages)) pendingWatchMessages[location] = messages = [];
            messages.Add(message);
            AddLog($"New {(location.DeadLetter ? "dead-letter" : "active")} message · {location.Path} · {message.MessageId}.");
        }
        ShowWatchNotification();
    }

    private void ShowWatchNotification()
    {
        if (!notificationsEnabled || pendingWatchMessages.Count == 0) return;
        var pending = pendingWatchMessages.First().Value;
        if (watchNotification is null)
        {
            watchNotification = new WatchNotificationWindow(InvestigateWatchNotification, DismissWatchNotification);
            ProfileTheme.Apply(watchNotification, connectionSettings.SelectedProfile.ColorHex);
            watchNotification.Closed += (_, _) => watchNotification = null;
        }
        watchNotification.Update(pending[^1], pending.Count, pendingWatchMessages.Count - 1, activeConnection?.Name ?? "Local emulator");
        if (!watchNotification.IsVisible) watchNotification.Show();
    }

    private void InvestigateWatchNotification()
    {
        if (pendingWatchMessages.Count == 0) return;
        var pending = pendingWatchMessages.First();
        RestorePrototype();
        if (!Workspace.IsConnected) { AddLog("Reconnect to investigate the watched message. Notification retained."); return; }
        InvestigateWatchedCases(pending.Value);
        pendingWatchMessages.Remove(pending.Key);
        AdvanceWatchNotification();
    }

    public bool OpenWatchedMessage(MessageRow message)
    {
        if (!Workspace.IsConnected || !Workspace.SnapshotMessages().Any(row => row.Key == message.Key && row.Source == message.Source))
        {
            AddLog($"Watched message {message.MessageId} is unavailable. Reconnect or dismiss the notification.");
            return false;
        }
        SearchBox.Clear(); Workspace.SetSearch("");
        if (!Workspace.OpenMessage(message))
        {
            AddLog($"Unable to open watched message {message.MessageId}. Notification retained.");
            return false;
        }
        SelectInitialEntity(); SynchronizeSelection(); UpdateInspector(); ConfigureTimer();
        MessageGrid.ScrollIntoView(message);
        AddLog($"Opened watched {(message.IsDeadLetter ? "dead-letter" : "active")} message {message.MessageId} · {message.Source}.");
        return true;
    }

    private void InvestigateWatchedCases(IReadOnlyList<MessageRow> messages)
    {
        if (messages.Count == 0) return;
        var keepSearch = Workspace.IsCorrelationSearch && Workspace.SearchQueryError.Length == 0;
        var byMessageId = keepSearch ? Workspace.SearchByMessageId : messages.All(message => string.IsNullOrWhiteSpace(message.CorrelationId));
        var queryText = keepSearch ? Workspace.CorrelationQuery : "";
        MessageSearchQuery.TryParse(queryText, out var existing, out _);
        foreach (var message in messages)
        {
            var useMessageId = string.IsNullOrWhiteSpace(message.CorrelationId);
            var id = useMessageId ? message.MessageId : message.CorrelationId;
            if (existing?.CoversLiteral(id, useMessageId, byMessageId) == true) continue;
            var prefix = useMessageId == byMessageId ? "" : useMessageId ? "message:" : "correlation:";
            var term = prefix + MessageSearchQuery.QuoteLiteral(id);
            queryText = queryText.Length == 0 ? term : queryText + " OR " + term;
            MessageSearchQuery.TryParse(queryText, out existing, out _);
        }
        SearchBox.Text = queryText;
        BeginGlobalSearch(byMessageId);
        pendingSearchFocusKey = messages[^1].Key;
        SuggestionsPopup.IsOpen = false;
    }

    private void DismissWatchNotification()
    {
        if (pendingWatchMessages.Count > 0) pendingWatchMessages.Remove(pendingWatchMessages.First().Key);
        AdvanceWatchNotification();
    }

    private void AdvanceWatchNotification()
    {
        if (pendingWatchMessages.Count == 0) { watchNotification?.Close(); watchNotification = null; }
        else ShowWatchNotification();
    }

    private void WatchSummary_Click(object sender, RoutedEventArgs e)
    {
        WatchPopup.IsOpen = false;
        var menu = NewWatchMenu(sender);
        WatchSummaryButton.ContextMenu = menu;
        menu.Items.Add(new MenuItem { Header = $"{watchedLocations.Count} watched location{(watchedLocations.Count == 1 ? "" : "s")}", IsEnabled = false });
        foreach (var location in watchedLocations.ToArray()) AddWatchChoice(menu, location.Path, $"{location.Path} · {(location.DeadLetter ? "DLQ" : "Active")}", location.DeadLetter);
        menu.Items.Add(new Separator { Margin = new Thickness(10, 5, 10, 5), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(213, 223, 234)), Height = 1 });
        var pending = new MenuItem { Header = $"Show notifications ({pendingWatchMessages.Values.Sum(messages => messages.Count)})", IsEnabled = pendingWatchMessages.Count > 0 && notificationsEnabled };
        pending.Click += (_, _) => ShowWatchNotification(); menu.Items.Add(pending);
        foreach (var item in menu.Items.OfType<MenuItem>()) item.Style = (Style)FindResource("WatchOverviewItemStyle");
        menu.IsOpen = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new PrototypeSettingsWindow(closeToTray, notificationsEnabled, tray is not null || proofLifetime,
            value => closeToTray = value, value =>
            {
                notificationsEnabled = value;
                if (value) ShowWatchNotification();
                else { watchNotification?.Close(); watchNotification = null; }
            }, connectionSettings, UseConnection, SaveProfilePreferences) { Owner = this };
        ProfileTheme.Apply(settingsWindow, connectionSettings.SelectedProfile.ColorHex);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }

    private void UseConnection(PrototypeConnectionProfile profile)
    {
        if (!ReferenceEquals(activeConnection, profile))
        {
            if (!ConfirmProfileWarning(profile))
            {
                connectionSettings.SelectedProfile = activeConnection!;
                RefreshConnectionPresentation();
                return;
            }
            acknowledgedProfileWarning = WarningIdentity(profile);
        }
        SavePreferences();
        connectionSettings.SelectedProfile = profile;
        RefreshConnectionPresentation();
        ConnectionName.Text = profile.Name;
        ConnectionName.ToolTip = profile.Name;
        if (ReferenceEquals(activeConnection, profile)) { SavePreferences(); return; }
        activeConnection = profile;
        ClearConnectionWarning();
        refreshTimer.Stop();
        watchTimer.Stop();
        watchRules.Clear();
        RestoreProfileWatch();
        globalWatchWindow?.Close(); globalWatchWindow = null;
        pendingWatchMessages.Clear();
        watchNotification?.Close();
        watchNotification = null;
        WatchPopup.IsOpen = SuggestionsPopup.IsOpen = false;
        SearchBox.Clear();
        loadedSearchMessages.Clear();
        drafts.Clear();
        pendingSearchFocusKey = null;
        ChangeScope(() =>
        {
            if (Workspace.IsConnected) Workspace.ToggleConnection();
            Workspace.ResetConnectionSimulation();
        });
        UpdateWatchSurface();
        AddLog($"Selected {profile.Name}. Connect to load its sample messages.");
        if (connectionSettings.AutoConnectOnSwitch) Connection_Click(this, new RoutedEventArgs());
        SavePreferences();
    }
}
