using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private GlobalWatchWindow? globalWatchWindow;
    private string? watchProfile;
    private EntityNode? watchPopupEntity;
    private bool updatingWatchChoices;
    private bool savingWatch;

    private WatchRuleEditor CreateWatchEditor() => new(workspace.Browse.Roots,
        workspace.Preferences.Watches.TryGetValue(workspace.SelectedProfile.Id, out var rules) ? rules : []);

    private void GlobalWatch_Click(object sender, RoutedEventArgs e)
    {
        WatchPopup.IsOpen = false;
        if (globalWatchWindow is not null) { globalWatchWindow.Activate(); return; }
        string profile = workspace.SelectedProfile.Id;
        watchProfile = profile;
        globalWatchWindow = new(workspace.Browse.Roots, CreateWatchEditor(), editor => SaveWatchAsync(profile, editor)) { Owner = this };
        ProfileTheme.Apply(globalWatchWindow, workspace.SelectedProfile.ColorHex);
        globalWatchWindow.Closed += (_, _) => globalWatchWindow = null;
        globalWatchWindow.Show();
    }

    private async Task<bool> SaveWatchAsync(string profile, WatchRuleEditor editor)
    {
        savingWatch = true;
        UpdateWatchSurface();
        try
        {
            await workspace.UpdateWatchRulesAsync(profile, editor.Rules);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception)
        {
            workspace.Log("Watch preferences could not be saved. Previous rules remain active.", true);
            return false;
        }
        finally { savingWatch = false; UpdateWatchSurface(); }
    }

    private void UpdateWatchSurface()
    {
        if (!ready) return;
        if (watchProfile is not null && watchProfile != workspace.SelectedProfile.Id)
        {
            globalWatchWindow?.Close();
            WatchPopup.IsOpen = false;
            watchPopupEntity = null;
        }
        var editor = CreateWatchEditor();
        if (globalWatchWindow is not null) globalWatchWindow.IsEnabled = !savingWatch;
        globalWatchWindow?.RefreshRules(workspace.Browse.Roots, editor);
        bool watching = !workspace.Search.IsActive && workspace.Browse.SelectedEntity is { } selected
            && (editor.IsWatched(selected, false) || editor.IsWatched(selected, true));
        WatchButtonLabel.Text = watching ? "Watching" : "Watch";
        WatchBell.Fill = watching ? Brushes.White : Brushes.Transparent;
        WatchOffSlash.Visibility = watching ? Visibility.Collapsed : Visibility.Visible;
        WatchButton.IsEnabled = workspace.IsConnected && !savingWatch;
        GlobalWatchButton.IsEnabled = !savingWatch;
        int count = editor.Targets.Select(target => target.Address).Distinct().Count();
        WatchSummaryButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WatchSummaryButton.IsEnabled = workspace.IsConnected && !savingWatch;
        WatchCount.Text = count.ToString();
        WatchSummaryButton.ToolTip = $"Overview of {count} watched entities and pending notifications";
        ShowWatchNotification();
        AutomationProperties.SetName(WatchSummaryButton, $"Overview of {count} watched entities");
        if (!workspace.IsConnected) WatchPopup.IsOpen = false;
        if (!WatchPopup.IsOpen) return;
        updatingWatchChoices = true;
        WatchActiveChoice.IsChecked = watchPopupEntity is not null && editor.IsWatched(watchPopupEntity, false);
        WatchDlqChoice.IsChecked = watchPopupEntity is not null && editor.IsWatched(watchPopupEntity, true);
        WatchActiveChoice.IsEnabled = WatchDlqChoice.IsEnabled = watchPopupEntity is not null && !savingWatch;
        StopWatchingButton.IsEnabled = !savingWatch && (WatchActiveChoice.IsChecked == true || WatchDlqChoice.IsChecked == true);
        updatingWatchChoices = false;
    }

    private void Watch_Click(object sender, RoutedEventArgs e)
    {
        watchProfile = workspace.SelectedProfile.Id;
        watchPopupEntity = workspace.Search.IsActive || workspace.Browse.SelectedEntity?.IsGroup != false
            ? null : workspace.Browse.SelectedEntity;
        WatchPopupTitle.Text = watchPopupEntity is null ? "Select a queue, topic or subscription to watch" : $"Watch {watchPopupEntity.Name}";
        WatchPopup.IsOpen = true;
        UpdateWatchSurface();
    }

    private async void WatchChoice_Click(object sender, RoutedEventArgs e)
    {
        if (updatingWatchChoices || savingWatch || watchPopupEntity is null || !workspace.IsConnected) return;
        var editor = CreateWatchEditor();
        editor.SetScope(watchPopupEntity, ReferenceEquals(sender, WatchDlqChoice), ((CheckBox)sender).IsChecked == true);
        await SaveWatchAsync(workspace.SelectedProfile.Id, editor);
    }

    private async void StopWatching_Click(object sender, RoutedEventArgs e)
    {
        if (savingWatch || watchPopupEntity is null) return;
        var editor = CreateWatchEditor();
        editor.SetScope(watchPopupEntity, false, false);
        editor.SetScope(watchPopupEntity, true, false);
        await SaveWatchAsync(workspace.SelectedProfile.Id, editor);
        WatchPopup.IsOpen = false;
    }

    private void WatchSummary_Click(object sender, RoutedEventArgs e)
    {
        WatchPopup.IsOpen = false;
        var targets = CreateWatchEditor().Targets;
        var menu = new ContextMenu { PlacementTarget = WatchSummaryButton, Placement = PlacementMode.Bottom,
            Style = (Style)FindResource("WatchOverviewMenuStyle") };
        menu.Items.Add(new MenuItem { Header = $"{targets.Count} watched location{(targets.Count == 1 ? "" : "s")}", IsEnabled = false });
        foreach (var target in targets)
        {
            var node = workspace.Browse.AllEntities().First(entity => entity.Address == target.Address);
            var item = new MenuItem { Header = $"{node.Path} · {(target.Bucket == MessageBucket.DeadLetter ? "DLQ" : "Active")}", IsCheckable = true, IsChecked = true };
            item.Click += async (_, _) =>
            {
                var editor = CreateWatchEditor();
                editor.SetScope(node, target.Bucket == MessageBucket.DeadLetter, item.IsChecked);
                await SaveWatchAsync(workspace.SelectedProfile.Id, editor);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator { Margin = new Thickness(10, 5, 10, 5), Background = new SolidColorBrush(Color.FromRgb(213, 223, 234)), Height = 1 });
        var notifications = new MenuItem { Header = $"Show notifications ({workspace.Watch.PendingArrivals.Count})",
            IsEnabled = workspace.Watch.PendingArrivals.Count > 0 && workspace.Preferences.NotificationsEnabled };
        notifications.Click += (_, _) => ShowWatchNotification();
        menu.Items.Add(notifications);
        foreach (var item in menu.Items.OfType<MenuItem>()) item.Style = (Style)FindResource("WatchOverviewItemStyle");
        WatchSummaryButton.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
