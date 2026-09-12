using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow : Window
{
    private readonly InvestigationWorkspace workspace;
    private readonly DispatcherTimer refreshTimer = new();
    private bool ready;
    private bool updating;
    private bool paused;
    private bool closing;

    public InvestigationWindow(InvestigationWorkspace workspace)
    {
        this.workspace = workspace;
        InitializeComponent();
        DataContext = workspace.Surface;
        workspace.ConfirmWarning = profile => Task.FromResult(new ProfileWarningWindow(profile.Connection.Name,
            profile.WarningMessage, profile.ColorHex) { Owner = this }.ShowDialog() == true);
        workspace.ConfirmDiscard = () => Task.FromResult(new ProfileWarningWindow(workspace.SelectedProfile.Connection.Name,
            "Discard all unsaved message drafts and continue? The original broker messages will remain unchanged.",
            workspace.SelectedProfile.ColorHex, "Unsaved message drafts", "Discard") { Owner = this }.ShowDialog() == true);
        workspace.PropertyChanged += WorkspaceChanged;
        workspace.Surface.PropertyChanged += SurfaceChanged;
        workspace.Inspector.PropertyChanged += InspectorChanged;
        workspace.Activity.CollectionChanged += ActivityChanged;
        refreshTimer.Tick += RefreshTimerTick;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
        // These controls are connected in the Watch and mutation milestones.
        foreach (var button in new[] { GlobalWatchButton, WatchSummaryButton, WatchButton, ReplayButton, DeleteButton })
        {
            button.IsEnabled = false;
            button.ToolTip = "This workflow is not available in this integration build yet.";
        }
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        ready = true;
        await workspace.InitializeAsync();
        Width = workspace.Preferences.WindowWidth;
        Height = workspace.Preferences.WindowHeight;
        UpdateWorkspace();
        UpdateLayoutMode();
        UpdateInspector();
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InvestigationWorkspace.Status))
        {
            LastOperation.Text = workspace.Status;
            return;
        }
        UpdateWorkspace();
    }

    private void UpdateWorkspace()
    {
        if (!ready) return;
        updating = true;
        try
        {
            ConnectionSelector.ItemsSource = workspace.Preferences.Profiles;
            ConnectionSelector.SelectedItem = workspace.SelectedProfile;
            ConnectionHealthText.Text = workspace.HealthText;
            ConnectionButton.ToolTip = workspace.HealthDetail;
            ConnectionHealthDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                workspace.IsConnecting || workspace.HealthText == "Warning" ? "WarningHealthBrush" :
                workspace.IsConnected ? "ConnectedHealthBrush" : "DisconnectedHealthBrush");
            ProfileTheme.Apply(this, workspace.SelectedProfile.ColorHex);
            LastOperation.Text = workspace.Status;
            TimeDisplaySelector.SelectedIndex = workspace.Preferences.TimestampDisplay == TimestampDisplay.Utc ? 0 : 1;
            AutoInterval.SelectedIndex = Array.IndexOf(new[] { 0, 5, 10, 30 }, workspace.Preferences.AutoRefreshSeconds);
            LogPanel.Visibility = workspace.Preferences.LogExpanded ? Visibility.Visible : Visibility.Collapsed;
            LogHeading.Text = workspace.Preferences.LogExpanded ? "Activity log · Expanded" : "Activity log · Collapsed";
            LogChevron.Data = Geometry.Parse(workspace.Preferences.LogExpanded ? "M1,8 L7,2 L13,8" : "M1,2 L7,8 L13,2");
            EnqueuedColumn.Header = workspace.Preferences.TimestampDisplay == TimestampDisplay.Utc ? "Enqueued (UTC)" : "Enqueued (Local)";
            ConfigureRefresh();
            RenderActivity();
            UpdateSearchSurface();
        }
        finally { updating = false; }
    }

    private void SurfaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ready) return;
        ActiveTab.IsChecked = !workspace.Surface.IsDeadLetter;
        DeadLetterTab.IsChecked = workspace.Surface.IsDeadLetter;
        SourceColumn.Visibility = workspace.Surface.ShowsSource ? Visibility.Visible : Visibility.Collapsed;
        if (MessageGrid.SelectedItem != workspace.Surface.FocusedMessage)
            MessageGrid.SelectedItem = workspace.Surface.FocusedMessage;
        EmptyResults.Visibility = workspace.Surface.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageGrid.Visibility = workspace.Surface.Messages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SelectAllBox.IsEnabled = workspace.Surface.Messages.Count > 0;
        SelectAllBox.IsChecked = workspace.Surface.SelectedCount == 0 ? false :
            workspace.Surface.SelectedCount == workspace.Surface.Messages.Count ? true : null;
        EmptyMessage.Text = workspace.Surface.IsBusy ? "Loading messages…" : workspace.IsConnected ? "No messages in this view" : "Connect to browse messages";
        EmptyDescription.Text = workspace.Surface.IsBusy ? "Reading without consuming messages." : "";
        EmptyClearButton.Visibility = Visibility.Collapsed;
        UpdateSearchSurface();
        UpdateInspector();
    }

    private void InspectorChanged(object? sender, PropertyChangedEventArgs e) => UpdateInspector();

    private async void ConnectionSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || ConnectionSelector.SelectedItem is not InvestigationProfile profile) return;
        await workspace.SwitchProfileAsync(profile);
        UpdateWorkspace();
    }

    private async void Connection_Click(object sender, RoutedEventArgs e)
    {
        if (workspace.IsConnected || workspace.IsConnecting) await workspace.DisconnectAsync();
        else await workspace.ConnectAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(workspace.Preferences, workspace.ApplyPreferencesAsync) { Owner = this }.ShowDialog();

    private async void Tree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (workspace.Search.IsActive)
        {
            if (e.NewValue is EntityNode { IsGroup: false } scope)
            {
                workspace.Search.SelectScope(scope);
                FindMessageScrollViewer(MessageGrid)?.ScrollToTop();
            }
            return;
        }
        if (e.NewValue is EntityNode node && !node.IsGroup && node != workspace.Browse.SelectedEntity)
        {
            await workspace.RunReadAsync(() => workspace.Browse.SelectAsync(node, workspace.Browse.IsDeadLetter));
            FindMessageScrollViewer(MessageGrid)?.ScrollToTop();
        }
    }

    private async void Active_Click(object sender, RoutedEventArgs e) => await SelectBucket(false);
    private async void DeadLetter_Click(object sender, RoutedEventArgs e) => await SelectBucket(true);
    private async Task SelectBucket(bool deadLetter)
    {
        if (workspace.Browse.SelectedEntity is not { } node) return;
        await workspace.RunReadAsync(() => workspace.Browse.SelectAsync(node, deadLetter));
        FindMessageScrollViewer(MessageGrid)?.ScrollToTop();
    }
    private async void LoadMore_Click(object sender, RoutedEventArgs e) => await workspace.RunReadAsync(workspace.Search.IsActive ? workspace.Search.ContinueAsync : workspace.Browse.LoadMoreAsync);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await workspace.RunReadAsync(workspace.Search.IsActive ? workspace.Search.RefreshAsync : workspace.Browse.RefreshAsync);

    private async void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || AutoInterval.SelectedIndex < 0) return;
        await workspace.UpdateDisplayPreferencesAsync(autoRefreshSeconds: new[] { 0, 5, 10, 30 }[AutoInterval.SelectedIndex]);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        paused = !paused;
        PauseButton.Content = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(paused ? "PlayGeometry" : "PauseGeometry"),
            Width = 14, Height = 14, Stretch = Stretch.Uniform, Stroke = (Brush)FindResource("IconBrush"), StrokeThickness = 1.8
        };
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, paused ? "Resume automatic refresh" : "Pause automatic refresh");
        ConfigureRefresh();
    }

    private void ConfigureRefresh()
    {
        refreshTimer.Stop();
        if (paused || workspace.Search.IsActive || !workspace.IsConnected || workspace.Preferences.AutoRefreshSeconds == 0) return;
        refreshTimer.Interval = TimeSpan.FromSeconds(workspace.Preferences.AutoRefreshSeconds);
        refreshTimer.Start();
    }

    private async void RefreshTimerTick(object? sender, EventArgs e)
    {
        if (!workspace.Search.IsActive && !workspace.Browse.IsBusy) await workspace.RunReadAsync(workspace.Search.IsActive ? workspace.Search.ContinueAsync : workspace.Browse.RefreshAsync);
    }

    private void GlobalWatch_Click(object sender, RoutedEventArgs e) { }
    private void WatchSummary_Click(object sender, RoutedEventArgs e) { }
    private void Watch_Click(object sender, RoutedEventArgs e) { }
    private void WatchChoice_Click(object sender, RoutedEventArgs e) { }
    private void StopWatching_Click(object sender, RoutedEventArgs e) { }
    private void Delete_Click(object sender, RoutedEventArgs e) { }
    private void Replay_Click(object sender, RoutedEventArgs e) { }


    private async void ToggleLog_Click(object sender, RoutedEventArgs e) =>
        await workspace.UpdateDisplayPreferencesAsync(logExpanded: !workspace.Preferences.LogExpanded);
    private void ClearLog_Click(object sender, RoutedEventArgs e) => workspace.Activity.Clear();
    private async void TimeDisplay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating) return;
        await workspace.UpdateDisplayPreferencesAsync(timestampDisplay: TimeDisplaySelector.SelectedIndex == 0 ? TimestampDisplay.Utc : TimestampDisplay.Local);
    }
    private void ActivityChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RenderActivity();
    private void RenderActivity()
    {
        if (!ready) return;
        LogText.Document.Blocks.Clear();
        foreach (var entry in workspace.Activity)
        {
            var time = workspace.Preferences.TimestampDisplay == TimestampDisplay.Utc ? entry.TimestampUtc : entry.TimestampUtc.ToLocalTime();
            LogText.Document.Blocks.Add(new Paragraph(new Run($"{time:yyyy-MM-dd HH:mm:ss zzz}  {entry.Message}")) { Margin = new Thickness(0, 0, 0, 4) });
        }
        var last = workspace.Activity.LastOrDefault();
        LastOperationTime.Text = last is null ? "" :
            (workspace.Preferences.TimestampDisplay == TimestampDisplay.Utc ? last.TimestampUtc : last.TimestampUtc.ToLocalTime()).ToString("HH:mm:ss zzz");
        LogText.ScrollToEnd();
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (workspace.Inspector.HasDrafts && !await workspace.ConfirmDiscard()) return;
        refreshTimer.Stop();
        var bounds = WindowState == WindowState.Normal ? new Rect(0, 0, ActualWidth, ActualHeight) : RestoreBounds;
        await workspace.UpdateWindowBoundsAsync(bounds.Width, bounds.Height);
        await workspace.DisposeAsync();
        workspace.PropertyChanged -= WorkspaceChanged;
        workspace.Surface.PropertyChanged -= SurfaceChanged;
        workspace.Inspector.PropertyChanged -= InspectorChanged;
        workspace.Activity.CollectionChanged -= ActivityChanged;
        closing = true;
        // Even an entirely synchronous save must leave WPF's current Closing event before closing again.
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
