using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private GlobalWatchWindow? globalWatchWindow;
    private string? watchProfile;
    private bool updatingWatchChoices;

    private WatchRuleEditor CreateWatchEditor() => watch.CreateRules();

    private void GlobalWatch_Click(object sender, RoutedEventArgs e)
    {
        WatchPopup.IsOpen = false;
        if (globalWatchWindow is not null) { globalWatchWindow.Activate(); return; }
        string profile = workspace.SelectedProfile.Id;
        watchProfile = profile;
        globalWatchWindow = new(workspace.Browse.Roots, CreateWatchEditor(), editor => watch.SaveAsync(profile, editor)) { Owner = this };
        ProfileTheme.Apply(globalWatchWindow, workspace.SelectedProfile.ColorHex);
        globalWatchWindow.Closed += (_, _) => globalWatchWindow = null;
        globalWatchWindow.Show();
    }

    private void WatchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WatchToolbarViewModel.IsSaving)) UpdateWatchSurface();
    }

    private void UpdateWatchSurface()
    {
        if (!ready) return;
        if (watchProfile is not null && watchProfile != workspace.SelectedProfile.Id)
        {
            globalWatchWindow?.Close();
            WatchPopup.IsOpen = false;
            watch.ClosePopup();
        }
        var editor = CreateWatchEditor();
        var state = watch.StateFor(editor);
        UpdateNamespaceIndicators(editor);
        if (globalWatchWindow is not null) globalWatchWindow.IsEnabled = !watch.IsSaving;
        globalWatchWindow?.RefreshRules(workspace.Browse.Roots, editor);
        WatchButtonLabel.Text = state.IsWatching ? "Watching" : "Watch";
        WatchBell.Fill = state.IsWatching ? Brushes.White : Brushes.Transparent;
        WatchOffSlash.Visibility = state.IsWatching ? Visibility.Collapsed : Visibility.Visible;
        WatchButton.IsEnabled = state.CanWatch;
        GlobalWatchButton.IsEnabled = state.CanOpenGlobalWatch;
        WatchSummaryButton.Visibility = state.WatchedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        WatchSummaryButton.IsEnabled = state.CanOpenOverview;
        WatchCount.Text = state.WatchedCount.ToString();
        WatchSummaryButton.ToolTip = $"Overview of {state.WatchedCount} watched entities and pending notifications";
        ShowWatchNotification();
        AutomationProperties.SetName(WatchSummaryButton, $"Overview of {state.WatchedCount} watched entities");
        if (!workspace.IsConnected) WatchPopup.IsOpen = false;
        if (!WatchPopup.IsOpen) return;
        updatingWatchChoices = true;
        WatchActiveChoice.IsChecked = state.PopupWatchesActive;
        WatchDlqChoice.IsChecked = state.PopupWatchesDeadLetter;
        WatchActiveChoice.IsEnabled = WatchDlqChoice.IsEnabled = state.CanChoose;
        StopWatchingButton.IsEnabled = state.CanStopWatching;
        updatingWatchChoices = false;
    }

    private void Watch_Click(object sender, RoutedEventArgs e)
    {
        watchProfile = workspace.SelectedProfile.Id;
        watch.OpenPopup();
        WatchPopupTitle.Text = watch.PopupTitle;
        WatchPopup.IsOpen = true;
        UpdateWatchSurface();
    }

    private async void WatchChoice_Click(object sender, RoutedEventArgs e)
    {
        if (updatingWatchChoices) return;
        await watch.SetPopupScopeAsync(ReferenceEquals(sender, WatchDlqChoice), ((CheckBox)sender).IsChecked == true);
    }

    private async void StopWatching_Click(object sender, RoutedEventArgs e)
    {
        if (await watch.StopWatchingAsync()) WatchPopup.IsOpen = false;
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
            item.Click += async (_, _) => await watch.SetScopeAsync(node, target.Bucket == MessageBucket.DeadLetter, item.IsChecked);
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
