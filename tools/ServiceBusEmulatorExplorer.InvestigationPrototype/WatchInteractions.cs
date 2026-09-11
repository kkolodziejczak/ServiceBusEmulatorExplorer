using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private readonly HashSet<(string Path, bool DeadLetter)> watchedLocations = [];
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

    private void InitializeWatch()
    {
        watchTimer.Tick += (_, _) => SimulateWatchedArrivals();
        Closing += OnPrototypeClosing;
    }

    public void ConfigureApplicationLifetime(bool proofMode)
    {
        proofLifetime = proofMode;
        if (proofMode) return;
        try { tray = new PrototypeTray(RestorePrototype, ExitPrototype); }
        catch (Exception error) when (error is System.IO.IOException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            closeToTray = false;
            AddLog($"System tray unavailable: {error.Message}. Closing will exit.");
        }
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
    }

    private void Watch_Click(object sender, RoutedEventArgs e)
    {
        var entity = Workspace.SelectedEntity;
        if (entity is null || entity.IsGroup || entity.Kind == "Topic" || Workspace.IsCorrelationSearch)
        {
            watchPopupPath = null;
            WatchPopupTitle.Text = "Select a queue or subscription to watch";
        }
        else
        {
            watchPopupPath = entity.Path;
            WatchPopupTitle.Text = $"Watch {entity.Name}";
        }
        WatchActiveChoice.IsEnabled = WatchDlqChoice.IsEnabled = watchPopupPath is not null && Workspace.IsConnected;
        updatingWatchChoices = true;
        WatchActiveChoice.IsChecked = watchedLocations.Contains((watchPopupPath ?? "", false));
        WatchDlqChoice.IsChecked = watchedLocations.Contains((watchPopupPath ?? "", true));
        updatingWatchChoices = false;
        StopWatchingButton.IsEnabled = WatchActiveChoice.IsChecked == true || WatchDlqChoice.IsChecked == true;
        WatchPopup.IsOpen = true;
    }

    private void WatchChoice_Click(object sender, RoutedEventArgs e)
    {
        if (updatingWatchChoices || watchPopupPath is null || !Workspace.IsConnected) return;
        var deadLetter = ReferenceEquals(sender, WatchDlqChoice);
        SetWatched(watchPopupPath, deadLetter, ((CheckBox)sender).IsChecked == true);
        StopWatchingButton.IsEnabled = watchedLocations.Any(location => location.Path == watchPopupPath);
    }

    private void StopWatching_Click(object sender, RoutedEventArgs e)
    {
        if (watchPopupPath is null) return;
        SetWatched(watchPopupPath, false, false);
        SetWatched(watchPopupPath, true, false);
        WatchPopup.IsOpen = false;
    }

    private static ContextMenu NewWatchMenu(object sender) => new() { PlacementTarget = sender as UIElement, Placement = PlacementMode.Bottom };

    private void AddWatchChoice(ContextMenu menu, string path, string label, bool deadLetter)
    {
        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = watchedLocations.Contains((path, deadLetter)), IsEnabled = Workspace.IsConnected };
        item.Click += (_, _) => SetWatched(path, deadLetter, item.IsChecked);
        menu.Items.Add(item);
    }

    public void SetWatched(string path, bool deadLetter, bool enabled)
    {
        if (enabled) watchedLocations.Add((path, deadLetter));
        else
        {
            watchedLocations.Remove((path, deadLetter));
            pendingWatchMessages.Remove((path, deadLetter));
            AdvanceWatchNotification();
        }
        if (watchedLocations.Count > 0 && !proofLifetime) watchTimer.Start(); else watchTimer.Stop();
        UpdateWatchSurface();
        AddLog($"{(enabled ? "Watching" : "Stopped watching")} {path} · {(deadLetter ? "DLQ" : "Active")}.");
    }

    private void UpdateWatchSurface()
    {
        if (!Workspace.IsConnected) WatchPopup.IsOpen = false;
        if (FindName("WatchButtonLabel") is not TextBlock label) return;
        var path = Workspace.SelectedEntity?.Path;
        label.Text = !Workspace.IsCorrelationSearch && watchedLocations.Any(location => location.Path == path) ? "Watching" : "Watch";
    }

    public void SimulateWatchedArrivals()
    {
        if (!Workspace.IsConnected) return;
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
            watchNotification.Closed += (_, _) => watchNotification = null;
        }
        watchNotification.Update(pending[^1], pending.Count, pendingWatchMessages.Count - 1);
        if (!watchNotification.IsVisible) watchNotification.Show();
    }

    private void InvestigateWatchNotification()
    {
        if (pendingWatchMessages.Count == 0) return;
        var pending = pendingWatchMessages.First();
        RestorePrototype();
        if (!Workspace.IsConnected) { AddLog("Reconnect to investigate the watched message. Notification retained."); return; }
        if (!OpenWatchedMessage(pending.Value[^1])) return;
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
        var menu = NewWatchMenu(sender);
        menu.Items.Add(new MenuItem { Header = $"{watchedLocations.Count} watched location{(watchedLocations.Count == 1 ? "" : "s")}", IsEnabled = false });
        foreach (var location in watchedLocations.ToArray()) AddWatchChoice(menu, location.Path, $"{location.Path} · {(location.DeadLetter ? "DLQ" : "Active")}", location.DeadLetter);
        var pending = new MenuItem { Header = $"Show notifications ({pendingWatchMessages.Values.Sum(messages => messages.Count)})", IsEnabled = pendingWatchMessages.Count > 0 && notificationsEnabled };
        pending.Click += (_, _) => ShowWatchNotification(); menu.Items.Add(pending);
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
            }, connectionSettings) { Owner = this };
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
}
