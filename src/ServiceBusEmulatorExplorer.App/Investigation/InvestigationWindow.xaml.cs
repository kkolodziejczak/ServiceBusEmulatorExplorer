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
    private bool synchronizingBucketTabs;

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
        workspace.Watch.PendingArrivals.CollectionChanged += WatchArrivalsChanged;
        refreshTimer.Tick += RefreshTimerTick;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        ready = true;
        await workspace.InitializeAsync();
        if (closing || closePending) return;
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
            UpdateWatchSurface();
            UpdateReplaySurface();
        }
        finally { updating = false; }
    }

    private void SurfaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ready) return;
        UpdateWatchSurface();
        SynchronizeBucketTabs();
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
        new SettingsWindow(workspace.Preferences, workspace.ApplyPreferencesAsync, tray is not null) { Owner = this }.ShowDialog();

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

    private void SynchronizeBucketTabs()
    {
        synchronizingBucketTabs = true;
        try
        {
            ActiveTab.IsChecked = !workspace.Surface.IsDeadLetter;
            DeadLetterTab.IsChecked = workspace.Surface.IsDeadLetter;
        }
        finally { synchronizingBucketTabs = false; }
    }

    private async void Active_Checked(object sender, RoutedEventArgs e)
    {
        if (ready && !synchronizingBucketTabs) await SelectBucket(false);
    }

    private async void DeadLetter_Checked(object sender, RoutedEventArgs e)
    {
        if (ready && !synchronizingBucketTabs) await SelectBucket(true);
    }

    private void Bucket_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ready && !synchronizingBucketTabs) SynchronizeBucketTabs();
    }

    private async Task SelectBucket(bool deadLetter)
    {
        if (workspace.Search.IsActive || workspace.Browse.SelectedEntity is not { } node)
        {
            SynchronizeBucketTabs();
            return;
        }
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
        if (paused || workspace.Search.IsActive || !workspace.IsConnected || workspace.Preferences.AutoRefreshSeconds == 0)
        {
            refreshTimer.Stop();
            return;
        }
        var interval = TimeSpan.FromSeconds(workspace.Preferences.AutoRefreshSeconds);
        if (refreshTimer.Interval != interval) refreshTimer.Interval = interval;
        if (!refreshTimer.IsEnabled) refreshTimer.Start();
    }

    private async void RefreshTimerTick(object? sender, EventArgs e)
    {
        if (!workspace.Search.IsActive && !workspace.Browse.IsBusy) await workspace.RunReadAsync(workspace.Search.IsActive ? workspace.Search.ContinueAsync : workspace.Browse.RefreshAsync);
    }



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
            var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            paragraph.Inlines.Add(new Run(ActivityTime(entry.TimestampUtc) + "   ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(135, 167, 191)),
                ToolTip = entry.TimestampUtc.ToString("O")
            });
            paragraph.Inlines.Add(new Run(entry.Warning ? "WARN    " : entry.Watch ? "WATCH   " : "INFO    ")
            {
                Foreground = entry.Warning ? Brushes.Orange : entry.Watch ? Brushes.Cyan : Brushes.LightGreen
            });
            paragraph.Inlines.Add(new Run(entry.Message));
            LogText.Document.Blocks.Add(paragraph);
        }
        var last = workspace.Activity.LastOrDefault();
        LastOperationTime.Text = last is null ? "" : ActivityTime(last.TimestampUtc);
        LastOperationTime.ToolTip = last?.TimestampUtc.ToString("O");
        LogText.ScrollToEnd();
    }

    private string ActivityTime(DateTimeOffset instant) => workspace.Preferences.TimestampDisplay == TimestampDisplay.Utc
        ? instant.ToString("HH:mm:ss") + " UTC"
        : instant.ToLocalTime().ToString("HH:mm:ss") + " Local";

}
