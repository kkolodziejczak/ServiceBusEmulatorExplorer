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
using ServiceBusEmulatorExplorer.Presentation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow : Window, IMessageActionsHost, IWatchNotificationHost
{
    private readonly InvestigationWorkspace workspace;
    private readonly MessageActionsViewModel actions;
    private readonly WatchToolbarViewModel watch;
    private readonly SearchSurfaceViewModel searchSurface;
    private readonly NamespaceScopeViewModel namespaceScope;
    private readonly MessageColumnsViewModel columns;
    private readonly InspectorPanelViewModel inspectorPanel;
    private readonly WatchNotificationsViewModel watchNotifications;
    private readonly ConnectionBarViewModel connectionBar;
    private readonly ActivityLogViewModel activityLog;
    private readonly MessageListToolbarViewModel listToolbar;
    private readonly WindowCloseViewModel windowClose;
    private readonly AutoRefreshTimer refreshTimer;
    private readonly Action<string> copyText;
    private bool ready;
    private bool updating;
    private bool closing;
    private bool synchronizingBucketTabs;
    private bool synchronizingWorkspaceTabs;
    private bool initializingWindow;
    private bool resizedDuringInitialize;
    private string investigationSearchText = "";
    private readonly EntityNode[] sampleNamespaceRoots = CreateSampleNamespaceRoots();
    private bool lastNamespaceConnectionState;
    private bool sampleWorkbenchDestinations;

    /// <param name="createTray">Creates the notification-area icon from its Open and Exit actions; the real tray by default.</param>
    /// <param name="copyText">Puts text on the clipboard, for this window and the windows it opens; the system clipboard by default.</param>
    public InvestigationWindow(InvestigationWorkspace workspace, Func<Action, Action, IDisposable>? createTray = null,
        Action<string>? copyText = null)
    {
        this.workspace = workspace;
        this.createTray = createTray ?? ((open, exit) => new InvestigationTray(open, exit));
        this.copyText = copyText ?? Clipboard.SetText;
        actions = new MessageActionsViewModel(workspace, this);
        actions.PropertyChanged += ActionsChanged;
        watch = new WatchToolbarViewModel(workspace);
        watch.PropertyChanged += WatchChanged;
        searchSurface = new SearchSurfaceViewModel(workspace);
        namespaceScope = new NamespaceScopeViewModel(workspace);
        columns = new MessageColumnsViewModel(workspace);
        columns.PropertyChanged += ColumnsChanged;
        inspectorPanel = new InspectorPanelViewModel(workspace, this.copyText);
        watchNotifications = new WatchNotificationsViewModel(workspace, this);
        watchNotifications.PropertyChanged += WatchNotificationsChanged;
        connectionBar = new ConnectionBarViewModel(workspace);
        activityLog = new ActivityLogViewModel(workspace);
        listToolbar = new MessageListToolbarViewModel(workspace);
        windowClose = new WindowCloseViewModel(workspace);
        refreshTimer = new(workspace.Clock, tick => Dispatcher.BeginInvoke(DispatcherPriority.Background, tick));
        InitializeComponent();
        MessageLibraryPrototype.ConnectionRepairRequested += () => ConnectionButton.Focus();
        lastNamespaceConnectionState = workspace.IsConnected;
        MessageLibraryPrototype.TemplateContextRequested += topic =>
        {
            if (MessageLibraryPrototype.Visibility == Visibility.Visible)
                SearchBox.Text = topic ?? "";
        };
        DataContext = workspace.Surface;
        workspace.ConfirmWarning = profile => Task.FromResult(new ProfileWarningWindow(profile.Connection.Name,
            profile.WarningMessage, profile.ColorHex) { Owner = this }.ShowDialog() == true);
        workspace.ConfirmDiscard = () => Task.FromResult(new ProfileWarningWindow(workspace.SelectedProfile.Connection.Name,
            "Discard all unsaved message drafts and continue? The original broker messages will remain unchanged.",
            workspace.SelectedProfile.ColorHex, "Unsaved message drafts", "Discard") { Owner = this }.ShowDialog() == true);
        workspace.PropertyChanged += WorkspaceChanged;
        workspace.Surface.PropertyChanged += SurfaceChanged;
        workspace.Browse.PropertyChanged += WorkbenchDiscoveryChanged;
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
        initializingWindow = true;
        UpdateWorkspace();
        SurfaceChanged(workspace.Surface, new PropertyChangedEventArgs(nameof(InvestigationSurface.Messages)));
        UpdateLayoutMode();
        await workspace.InitializeAsync();
        if (closing || closePending) return;
        initializingWindow = false;
        if (!resizedDuringInitialize)
        {
            Width = workspace.Preferences.WindowWidth;
            Height = workspace.Preferences.WindowHeight;
        }
        UpdateWorkspace();
        UpdateLayoutMode();
        UpdateInspector();
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InvestigationWorkspace.Status))
        {
            LastOperation.Text = connectionBar.LastOperation;
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
            MessageLibraryPrototype.SetProfileName(workspace.SelectedProfile.Connection.Name);
            UpdateWorkbenchDiscovery();
            ConnectionHealthText.Text = workspace.HealthText;
            ConnectionButton.ToolTip = workspace.HealthDetail;
            ConnectionHealthDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, connectionBar.Health switch
            {
                ConnectionHealth.Warning => "WarningHealthBrush",
                ConnectionHealth.Connected => "ConnectedHealthBrush",
                _ => "DisconnectedHealthBrush"
            });
            ProfileTheme.Apply(this, workspace.SelectedProfile.ColorHex);
            LastOperation.Text = connectionBar.LastOperation;
            DatePresentation.SetFormat(this, workspace.Preferences.DateFormat);
            TimeDisplaySelector.SelectedIndex = activityLog.UsesUtc ? 0 : 1;
            AutoInterval.SelectedIndex = listToolbar.IntervalIndex;
            LogPanel.Visibility = activityLog.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            LogToggle.ToolTip = activityLog.ToggleLabel;
            System.Windows.Automation.AutomationProperties.SetName(LogToggle, activityLog.ToggleLabel);
            LogHeading.Text = activityLog.Heading;
            LogChevron.Data = Geometry.Parse(activityLog.IsExpanded ? "M1,8 L7,2 L13,8" : "M1,2 L7,8 L13,2");
            EnqueuedColumn.Header = activityLog.EnqueuedHeader;
            UpdateMessageColumns();
            ConfigureRefresh();
            RenderActivity();
            UpdateSearchSurface();
            UpdateWatchSurface();
            UpdateReplaySurface();
            UpdateNamespaceTreeSource();
            UpdateBrowseScopeHeader();
            ScheduleNamespaceSelectionRestore();
            if (MessageLibraryPrototype.Visibility == Visibility.Visible)
                FilterWorkbenchNamespaces(SearchBox.Text);
        }
        finally { updating = false; }
    }

    private void SurfaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ready) return;
        if (!workspace.Surface.IsBusy && namespaceTreeReconciliation) ScheduleNamespaceSelectionRestore();
        UpdateWatchSurface();
        SynchronizeBucketTabs();
        UpdateMessageColumns();
        if (MessageGrid.SelectedItem != workspace.Surface.FocusedMessage)
            MessageGrid.SelectedItem = workspace.Surface.FocusedMessage;
        EmptyResults.Visibility = workspace.Surface.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageGrid.Visibility = workspace.Surface.Messages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SelectAllBox.IsEnabled = listToolbar.CanCheckAll;
        SelectAllBox.IsChecked = listToolbar.AllChecked;
        EmptyMessage.Text = workspace.Surface.IsBusy ? "Loading messages…" : workspace.IsConnecting ? "Connecting to Service Bus…"
            : workspace.IsConnected ? "No messages in this view" : "Connect to browse messages";
        EmptyDescription.Text = workspace.Surface.IsBusy ? "Reading without consuming messages."
            : workspace.IsConnecting ? "Discovering available entities." : "";
        EmptyClearButton.Visibility = Visibility.Collapsed;
        UpdateNamespaceTreeSource();
        UpdateBrowseScopeHeader();
        ScheduleNamespaceSelectionRestore();
        if (MessageLibraryPrototype.Visibility == Visibility.Visible)
            FilterWorkbenchNamespaces(SearchBox.Text);
        UpdateSearchSurface();
        UpdateInspector();
    }

    private void InspectorChanged(object? sender, PropertyChangedEventArgs e) => UpdateInspector();

    private async void ConnectionSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || ConnectionSelector.SelectedItem is not InvestigationProfile profile) return;
        await connectionBar.SwitchProfileAsync(profile);
        UpdateWorkspace();
    }

    private async void Connection_Click(object sender, RoutedEventArgs e) => await connectionBar.ToggleAsync();

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(workspace.Preferences, workspace.ApplyPreferencesAsync, tray is not null, copyText) { Owner = this }.ShowDialog();

    private void InvestigationWorkspace_Checked(object sender, RoutedEventArgs e) => SelectWorkspaceTab(false);
    private void MessageLibrary_Checked(object sender, RoutedEventArgs e) => SelectWorkspaceTab(true);

    internal void OpenMessageWorkbenchPrototype()
    {
        sampleWorkbenchDestinations = true;
        if (!workspace.IsConnected) MessageLibraryPrototype.UseSampleDestinations();
        SelectWorkspaceTab(true);
    }

    private void WorkbenchDiscoveryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageBrowseWorkflow.DiscoverySnapshot))
        {
            if (workspace.Browse.DiscoverySnapshot is null && workspace.Browse.SelectedEntity is not null)
                namespaceTreeReconciliation = true;
            else if (workspace.Browse.DiscoverySnapshot is not null)
                ScheduleNamespaceSelectionRestore();
            UpdateWorkbenchDiscovery();
            UpdateNamespaceIndicators();
        }
    }

    private void UpdateWorkbenchDiscovery()
    {
        if (workspace.IsConnected) sampleWorkbenchDestinations = false;
        if (sampleWorkbenchDestinations) return;
        var profile = workspace.SelectedProfile;
        string endpoint = profile.Connection.FullyQualifiedNamespace;
        if (endpoint.Length == 0)
        {
            string? endpointPart = profile.Connection.RuntimeConnectionString.Split(';')
                .FirstOrDefault(part => part.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase));
            if (endpointPart is not null && Uri.TryCreate(endpointPart[9..], UriKind.Absolute, out var address)) endpoint = address.Host;
        }
        var snapshot = workspace.IsConnected && workspace.Browse.DiscoveryGeneration == workspace.ConnectionGeneration
            ? workspace.Browse.DiscoverySnapshot : null;
        MessageLibraryPrototype.SetDestinationDiscovery(profile.Id, workspace.ConnectionGeneration, snapshot, endpoint);
    }

    private void SelectWorkspaceTab(bool library)
    {
        if (synchronizingWorkspaceTabs || ContentGrid is null) return;
        synchronizingWorkspaceTabs = true;
        try
        {
            bool enteringLibrary = ContentGrid.Visibility != Visibility.Collapsed && library;
            bool leavingLibrary = ContentGrid.Visibility == Visibility.Collapsed && !library;
            if (enteringLibrary) investigationSearchText = SearchBox.Text;
            InvestigationWorkspaceTab.IsChecked = !library;
            MessageLibraryTab.IsChecked = library;
            ContentGrid.Visibility = library ? Visibility.Collapsed : Visibility.Visible;
            MessageLibraryPrototype.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
            NamespaceTree.Visibility = Visibility.Visible;
            SearchPlaceholder.Text = library ? "Search namespaces" : "Entities, correlation or message ID";
            SearchBox.ToolTip = library ? "Search namespace entities and filter associated templates" :
                "Search entities, correlation IDs or message IDs across this connection";
            System.Windows.Automation.AutomationProperties.SetName(SearchBox,
                library ? "Search namespace entities" : "Search all entities and messages");
            NamespaceModeLabel.Visibility = library && !workspace.IsConnected ? Visibility.Visible : Visibility.Collapsed;
            NamespaceModeLabel.Text = library && !workspace.IsConnected
                ? "Sample entities · offline; live counts are unavailable"
                : "";
            UpdateNamespaceTreeSource();
            UpdateWorkspaceColumns();
            NamespaceEmpty.Visibility = library ? Visibility.Collapsed : NamespaceEmpty.Visibility;
            if (enteringLibrary) SearchBox.Text = "";
            if (leavingLibrary)
            {
                SearchBox.Text = investigationSearchText;
                workspace.Browse.FilterEntities(workspace.Search.IsActive ? "" : investigationSearchText);
                if (!workspace.Search.IsActive) workspace.Browse.RestoreWatchedMessages();
            }
            if (library) FilterWorkbenchNamespaces(SearchBox.Text);
            ConfigureRefresh();
            UpdateSearchSurface();
        }
        finally { synchronizingWorkspaceTabs = false; }
    }

    private void UpdateNamespaceTreeSource()
    {
        if (NamespaceTree is null || MessageLibraryPrototype is null) return;
        bool library = MessageLibraryPrototype.Visibility == Visibility.Visible;
        bool disconnected = lastNamespaceConnectionState && !workspace.IsConnected;

        lastNamespaceConnectionState = workspace.IsConnected;
        IEnumerable<EntityNode> roots = library
            ? workspace.IsConnected ? workspace.Browse.Roots : sampleNamespaceRoots
            : workspace.Surface.Roots;
        var source = roots as object;
        if (!ReferenceEquals(NamespaceTree.ItemsSource, source)) NamespaceTree.ItemsSource = roots;
        if (library && disconnected)
        {
            SearchBox.Clear();
            MessageLibraryPrototype.SelectEntityContext("topic:order-events");
        }
        NamespaceModeLabel.Visibility = library && !workspace.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        NamespaceModeLabel.Text = library && !workspace.IsConnected
            ? "Sample entities · offline; live counts are unavailable"
            : "";
    }

    private void FilterWorkbenchNamespaces(string query)
    {
        if (MessageLibraryPrototype is null) return;
        IEnumerable<EntityNode> roots = workspace.IsConnected ? workspace.Browse.Roots : sampleNamespaceRoots;
        if (workspace.IsConnected) workspace.Browse.FilterEntities(query);
        foreach (var group in roots)
        {
            bool any = false;
            foreach (var entity in group.Children)
            {
                string entityPath = WorkbenchPath(entity, roots);
                bool queryMatchesEntity = query.Length == 0 || entityPath.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool childMatch = false;
                foreach (var child in entity.Children)
                {
                    bool queryMatchesChild = query.Length == 0 || queryMatchesEntity || WorkbenchPath(child, roots).Contains(query, StringComparison.OrdinalIgnoreCase);
                    child.IsVisible = queryMatchesChild;
                    childMatch |= child.IsVisible;
                }
                entity.IsVisible = queryMatchesEntity || childMatch;
                any |= entity.IsVisible;
            }
            group.IsVisible = any;
        }
        string libraryQuery = query;
        if (query.Length > 0)
        {
            var matchingSubscription = roots.SelectMany(group => group.Children)
                .SelectMany(entity => entity.Children)
                .FirstOrDefault(child => WorkbenchPath(child, roots).Contains(query, StringComparison.OrdinalIgnoreCase));
            if (matchingSubscription is not null) libraryQuery = WorkbenchPath(matchingSubscription, roots).Split('/')[0];
        }
        MessageLibraryPrototype.FilterByNamespaceQuery(libraryQuery);
    }

    private static EntityNode[] CreateSampleNamespaceRoots()
    {
        var queues = new EntityNode("Queues", "Group");
        queues.Children.Add(new EntityNode("order-replies", "Queue"));
        var topics = new EntityNode("Topics", "Group");
        var orders = new EntityNode("order-events", "Topic");
        orders.Children.Add(new EntityNode("billing", "Subscription"));
        orders.Children.Add(new EntityNode("analytics", "Subscription"));
        orders.Children.Add(new EntityNode("notifications", "Subscription"));
        topics.Children.Add(orders);
        var inventory = new EntityNode("inventory-events", "Topic");
        inventory.Children.Add(new EntityNode("warehouse", "Subscription"));
        topics.Children.Add(inventory);
        return [queues, topics];
    }

    private static string WorkbenchPath(EntityNode node, IEnumerable<EntityNode> roots)
    {
        if (node.Kind != "Subscription") return node.Path;
        var parent = roots.SelectMany(root => root.Children).FirstOrDefault(entity => entity.Children.Contains(node));
        return parent is null ? node.Path : $"{parent.Name}/{node.Name}";
    }

    private static string WorkbenchIdentity(EntityNode node, IEnumerable<EntityNode> roots) => node.Kind switch
    {
        "Subscription" => $"subscription:{WorkbenchPath(node, roots)}",
        "Topic" => $"topic:{node.Name}",
        _ => $"queue:{node.Name}"
    };

    private async void Tree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (restoringNamespaceSelection || namespaceTreeReconciliation && !NamespaceTree.IsKeyboardFocusWithin) return;
        if (e.NewValue is EntityNode { Address: { } selectedAddress })
            namespaceScope.Remember(selectedAddress);

        if (MessageLibraryPrototype.Visibility == Visibility.Visible)
        {
            if (e.NewValue is EntityNode { IsGroup: false } workbenchEntity)
            {
                IEnumerable<EntityNode> roots = workspace.IsConnected ? workspace.Browse.Roots : sampleNamespaceRoots;
                string identity = WorkbenchIdentity(workbenchEntity, roots);
                SearchBox.Text = WorkbenchPath(workbenchEntity, roots);
                MessageLibraryPrototype.SelectEntityContext(identity);
            }
            return;
        }
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
        if (!await listToolbar.SelectBucketAsync(deadLetter))
        {
            SynchronizeBucketTabs();
            return;
        }
        FindMessageScrollViewer(MessageGrid)?.ScrollToTop();
    }
    private async void LoadMore_Click(object sender, RoutedEventArgs e) => await listToolbar.LoadMoreAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await listToolbar.RefreshAsync();

    private async void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating || AutoInterval.SelectedIndex < 0) return;
        await listToolbar.SetIntervalAsync(AutoInterval.SelectedIndex);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        listToolbar.TogglePause();
        PauseButton.Content = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(listToolbar.IsPaused ? "PlayGeometry" : "PauseGeometry"),
            Width = 14, Height = 14, Stretch = Stretch.Uniform, Stroke = (Brush)FindResource("IconBrush"), StrokeThickness = 1.8
        };
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, listToolbar.PauseLabel);
        ConfigureRefresh();
    }

    private void ConfigureRefresh()
    {
        UpdateNamespaceIndicators();
        if (listToolbar.RefreshInterval(MessageLibraryPrototype?.Visibility == Visibility.Visible) is not { } interval)
        {
            refreshTimer.Stop();
            listToolbar.CancelBackgroundRefresh();
            return;
        }
        refreshTimer.Start(interval);
    }

    private async void RefreshTimerTick() => await listToolbar.TickAsync(MessageLibraryPrototype.Visibility == Visibility.Visible);



    private async void ToggleLog_Click(object sender, RoutedEventArgs e) => await activityLog.ToggleAsync();
    private void ClearLog_Click(object sender, RoutedEventArgs e) => activityLog.Clear();
    private async void TimeDisplay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updating) return;
        await activityLog.SetUsesUtcAsync(TimeDisplaySelector.SelectedIndex == 0);
    }
    private void ActivityChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RenderActivity();
    private void RenderActivity()
    {
        if (!ready) return;
        ClearLogButton.IsEnabled = !activityLog.IsEmpty;
        LogEmpty.Visibility = activityLog.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        LogText.Document.Blocks.Clear();
        foreach (var line in activityLog.Lines)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            paragraph.Inlines.Add(new Run(line.Time + "   ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(135, 167, 191)),
                ToolTip = line.Date
            });
            paragraph.Inlines.Add(new Run(line.Label)
            {
                Foreground = line.Level switch
                {
                    ActivityLevel.Warning => Brushes.Orange,
                    ActivityLevel.Watch => Brushes.Cyan,
                    _ => Brushes.LightGreen
                }
            });
            paragraph.Inlines.Add(new Run(line.Message));
            LogText.Document.Blocks.Add(paragraph);
        }
        LastOperationTime.Text = activityLog.LastTime;
        LastOperationTime.ToolTip = activityLog.LastDate;
        LogText.ScrollToEnd();
    }

}
