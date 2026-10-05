using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationScopeColumnsRenderTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Browse_scope_tree_selection_survives_focus_and_entity_reconciliation()
        => RunSta(() =>
        {
            EntityDiscoverySnapshot first = Snapshot();
            EntityDiscoverySnapshot refreshed = Snapshot();
            var browser = new FakeBrowser(first, refreshed);
            var workspace = CreateWorkspace(browser);
            var window = new InvestigationWindow(workspace)
            {
                Width = 1200,
                Height = 800,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            Drain();
            Complete(workspace.ConnectAsync());
            window.Width = 1200;
            window.Height = 800;
            window.UpdateLayout();

            try
            {
                EntityNode subscription = workspace.Browse.AllEntities().Single(node => node.Kind == "Subscription" && node.Name == "billing");
                EntityNode siblingTopic = workspace.Browse.AllEntities().Single(node => node.Kind == "Topic" && node.Name == "archive-events");
                siblingTopic.IsExpanded = false;
                TreeView tree = (TreeView)window.FindName("NamespaceTree")!;
                TreeViewItem item = FindTreeItem(tree, subscription);
                item.IsSelected = true;
                Drain();
                Assert.Same(subscription, workspace.Browse.SelectedEntity);
                Assert.True(item.IsSelected);

                // Focusing a delivery inspects it without changing the selected browse scope.
                DataGrid grid = (DataGrid)window.FindName("MessageGrid")!;
                grid.Focus();
                grid.SelectedItem = workspace.Browse.Messages.Single();
                Drain();
                Assert.Same(workspace.Browse.Messages.Single(), workspace.Browse.FocusedMessage);
                Assert.Same(subscription, workspace.Browse.SelectedEntity);
                Assert.True(item.IsSelected);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("EmptyInspector")!).Visibility);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("InspectorHeading")!).Visibility);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BodyEditor")!).Visibility);

                Complete(workspace.Browse.RefreshAsync());
                Drain();
                EntityNode reconciled = workspace.Browse.SelectedEntity!;
                Assert.Equal(subscription.Address, reconciled.Address);
                Assert.True(FindTreeItem(tree, reconciled).IsSelected);
                Assert.False(siblingTopic.IsExpanded);
            }
            finally
            {
                window.Close();
                Complete(workspace.DisposeAsync().AsTask());
            }
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Scope_header_and_compact_columns_preserve_hierarchy_order_and_copy_adjacency()
        => RunSta(() =>
        {
            EntityDiscoverySnapshot initial = Snapshot();
            string[] order = [MessageColumnCatalog.Event, MessageColumnCatalog.Enqueued, MessageColumnCatalog.Correlation];
            var workspace = CreateWorkspace(new FakeBrowser(initial), order);
            var window = new InvestigationWindow(workspace)
            {
                Width = 1500,
                Height = 1000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            Drain();
            Complete(workspace.ConnectAsync());
            window.Width = 1500;
            window.Height = 1000;
            window.UpdateLayout();
            Drain();

            try
            {
                TreeView tree = (TreeView)window.FindName("NamespaceTree")!;
                EntityNode subscription = workspace.Browse.AllEntities().Single(node => node.Kind == "Subscription" && node.Name == "billing");
                SelectTreeScope(tree, subscription);
                DataGrid grid = (DataGrid)window.FindName("MessageGrid")!;
                grid.Focus();
                grid.SelectedItem = workspace.Browse.Messages.Single();
                Drain();

                Assert.Equal("orders", ((TextBlock)window.FindName("ScopeTopicName")!).Text);
                Assert.Equal("billing", ((TextBlock)window.FindName("ScopeEntityName")!).Text);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ScopeTopicLine")!).Visibility);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("ListBreadcrumb")!).Visibility);
                Assert.Equal("Topic: orders; Subscription: billing",
                    System.Windows.Automation.AutomationProperties.GetName((FrameworkElement)window.FindName("ScopeHeader")!));

                var visibleData = VisibleDataColumns(window, grid);
                Assert.Equal(new[] { "Event / Message ID", "Enqueued (UTC)", "Correlation ID" },
                    visibleData.Select(column => column.Header?.ToString()));
                Assert.All(visibleData, column =>
                {
                    Assert.Equal(DataGridLengthUnitType.Auto, column.Width.UnitType);
                    Assert.InRange(column.MaxWidth, column.MinWidth, 200);
                    Assert.InRange(column.ActualWidth, column.MinWidth, 200);
                });

                DataGridRow row = Row(grid, 0);
                DataGridColumn correlation = (DataGridColumn)window.FindName("CorrelationColumn")!;
                DataGridCell cell = Descendants<DataGridCell>(row).Single(candidate => candidate.Column == correlation);
                TextBlock value = Descendants<TextBlock>(cell).Single(candidate => candidate.Text == "order-1");
                Button copy = Descendants<Button>(cell).Single(candidate =>
                    System.Windows.Automation.AutomationProperties.GetName(candidate) == "Copy row correlation ID");
                Rect valueBounds = value.TransformToAncestor(cell).TransformBounds(new Rect(value.RenderSize));
                Rect copyBounds = copy.TransformToAncestor(cell).TransformBounds(new Rect(copy.RenderSize));
                Assert.InRange(copyBounds.Left - valueBounds.Right, -1, 8);
                Assert.True(copyBounds.Right <= cell.ActualWidth + 1);

                DataGridColumnHeader lastHeader = Descendants<DataGridColumnHeader>(grid).Single(header => header.Column == visibleData[^1]);
                Rect lastBounds = lastHeader.TransformToAncestor(grid).TransformBounds(new Rect(lastHeader.RenderSize));
                double visibleWidth = grid.Columns.Where(column => column.Visibility == Visibility.Visible)
                    .Sum(column => column.ActualWidth);
                if (visibleWidth <= grid.ActualWidth + 1)
                {
                    Assert.True(lastBounds.Right < grid.ActualWidth - 8,
                        $"A fitting set of real columns should leave an empty trailing remainder; column ended at {lastBounds.Right} of {grid.ActualWidth}.");
                }
                else
                {
                    ScrollViewer viewer = Descendants<ScrollViewer>(grid).First();
                    Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
                    Assert.True(viewer.ScrollableWidth > 0,
                        $"A compact viewport should scroll real columns instead of stretching the last one; grid={grid.ActualWidth}, content={visibleWidth}.");
                }
                CaptureIfRequested(window, "location44-replay45-subscription-1500x1000.png");

                EntityNode topic = workspace.Browse.AllEntities().Single(node => node.Kind == "Topic" && node.Name == "orders");
                SelectTreeScope(tree, topic);
                Assert.Equal("All subscriptions", ((TextBlock)window.FindName("ScopeAllSubscriptions")!).Text);
                Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("ScopeAllSubscriptions")!).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ScopeEntityLine")!).Visibility);
                CaptureIfRequested(window, "location44-topic-1500x1000.png");

                EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == "Queue");
                SelectTreeScope(tree, queue);
                Assert.Equal("order-archive", ((TextBlock)window.FindName("ScopeEntityName")!).Text);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ScopeTopicLine")!).Visibility);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ScopeEntityLine")!).Visibility);
                CaptureIfRequested(window, "location44-queue-1500x1000.png");

                Complete(workspace.Search.StartAsync("billing-1", defaultMessageId: true));
                Drain();
                Assert.Equal("Search / Related messages", ((TextBlock)window.FindName("ListBreadcrumb")!).Text);
                Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("ListBreadcrumb")!).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("BrowseScopeHeader")!).Visibility);

                EntityNode searchSubscription = workspace.Surface.Roots.SelectMany(root => root.Children)
                    .SelectMany(node => new[] { node }.Concat(node.Children))
                    .Single(node => node.Kind == "Subscription" && node.Name == "billing");
                SelectTreeScope(tree, searchSubscription);
                Assert.Equal("Search / Related messages", ((TextBlock)window.FindName("ListBreadcrumb")!).Text);
                Assert.Equal("Search / Related messages",
                    System.Windows.Automation.AutomationProperties.GetName((FrameworkElement)window.FindName("ScopeHeader")!));
                CaptureIfRequested(window, "location44-search-1500x1000.png");
            }
            finally
            {
                window.Close();
                Complete(workspace.DisposeAsync().AsTask());
            }
        });

    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Real_columns_stay_content_sized_and_scroll_inside_the_grid(int width, int height)
        => RunSta(() =>
        {
            var workspace = CreateWorkspace(new FakeBrowser(Snapshot()),
                [MessageColumnCatalog.Event, MessageColumnCatalog.Enqueued, MessageColumnCatalog.Correlation]);
            var window = new InvestigationWindow(workspace)
            {
                Width = width,
                Height = height,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            Drain();
            Complete(workspace.ConnectAsync());
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
            EntityNode subscription = workspace.Browse.AllEntities().Single(node => node.Kind == "Subscription" && node.Name == "billing");
            Complete(workspace.Browse.SelectAsync(subscription, false));
            Drain();

            try
            {
                DataGrid grid = (DataGrid)window.FindName("MessageGrid")!;
                var data = VisibleDataColumns(window, grid);
                Assert.Equal(2, grid.FrozenColumnCount);
                Assert.Equal(40, grid.Columns[0].ActualWidth);
                Assert.Equal(64, ((DataGridColumn)window.FindName("StateColumn")!).ActualWidth);
                Assert.All(data, column =>
                {
                    Assert.Equal(DataGridLengthUnitType.Auto, column.Width.UnitType);
                    Assert.InRange(column.MaxWidth, column.MinWidth, 200);
                    Assert.InRange(column.ActualWidth, column.MinWidth, 200);
                });

                double totalWidth = grid.Columns.Where(column => column.Visibility == Visibility.Visible).Sum(column => column.ActualWidth);
                if (totalWidth > grid.ActualWidth + 1)
                {
                    ScrollViewer viewer = Descendants<ScrollViewer>(grid).First();
                    Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
                    CaptureIfRequested(window, $"replay45-columns-{width}x{height}-left.png");

                    viewer.ScrollToRightEnd();
                    window.UpdateLayout();
                    grid.UpdateLayout();
                    Drain();
                    Assert.True(viewer.HorizontalOffset > 0);

                    DataGridColumn correlation = (DataGridColumn)window.FindName("CorrelationColumn")!;
                    DataGridColumnHeader header = Descendants<DataGridColumnHeader>(grid)
                        .Single(candidate => candidate.Column == correlation);
                    Rect headerBounds = header.TransformToAncestor(grid).TransformBounds(new Rect(header.RenderSize));
                    Assert.True(headerBounds.Left >= 103 && headerBounds.Right <= grid.ActualWidth + 1,
                        $"Correlation header should be fully visible after horizontal scrolling; bounds={headerBounds}, grid={grid.ActualWidth}.");

                    DataGridRow row = Row(grid, 0);
                    DataGridCell cell = Descendants<DataGridCell>(row).Single(candidate => candidate.Column == correlation);
                    Button copy = Descendants<Button>(cell).Single(candidate =>
                        System.Windows.Automation.AutomationProperties.GetName(candidate) == "Copy row correlation ID");
                    Rect copyBounds = copy.TransformToAncestor(grid).TransformBounds(new Rect(copy.RenderSize));
                    Assert.True(copy.IsVisible && copyBounds.Left >= 103 && copyBounds.Right <= grid.ActualWidth + 1,
                        $"Correlation Copy should be fully visible after horizontal scrolling; bounds={copyBounds}, grid={grid.ActualWidth}.");
                    CaptureIfRequested(window, $"replay45-columns-{width}x{height}-right.png");
                }
                else
                {
                    DataGridColumn correlation = (DataGridColumn)window.FindName("CorrelationColumn")!;
                    DataGridColumnHeader header = Descendants<DataGridColumnHeader>(grid)
                        .Single(candidate => candidate.Column == correlation);
                    Rect headerBounds = header.TransformToAncestor(grid).TransformBounds(new Rect(header.RenderSize));
                    Assert.True(headerBounds.Right < grid.ActualWidth - 8,
                        $"When the measured viewport fits the real columns, the correlation column should leave an empty trailing remainder; bounds={headerBounds}, grid={grid.ActualWidth}.");
                    CaptureIfRequested(window, $"replay45-columns-{width}x{height}.png");
                }
            }
            finally
            {
                window.Close();
                Complete(workspace.DisposeAsync().AsTask());
            }
        });

    private static InvestigationWorkspace CreateWorkspace(FakeBrowser browser, IReadOnlyList<string>? columnOrder = null)
    {
        var preferences = new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile("scope-proof", new ConnectionProfile("Scope proof", "runtime", "admin"))],
            SelectedProfileId = "scope-proof",
            WindowWidth = 1200,
            WindowHeight = 800,
            WasConnected = false,
            MessageColumns = columnOrder is null ? null : [.. columnOrder],
            MessageColumnOrder = columnOrder is null ? null : [.. columnOrder]
        };
        var workflow = new BrokerConnectionWorkflow(() => new FakeFactory(), _ => browser, _ => new FakeMessages());
        return new InvestigationWorkspace(new FakeStore(preferences), workflow);
    }

    private static EntityDiscoverySnapshot Snapshot()
    {
        ServiceBusEntityNode topic = new(EntityKind.Topic, "orders", null, new(2, 0, 0, 2), Metadata("orders"));
        ServiceBusEntityNode subscription = new(EntityKind.Subscription, "billing", "orders", new(2, 0, 0, 2), Metadata("orders/subscriptions/billing"));
        ServiceBusEntityNode archiveTopic = new(EntityKind.Topic, "archive-events", null, new(1, 0, 0, 1), Metadata("archive-events"));
        ServiceBusEntityNode archiveSubscription = new(EntityKind.Subscription, "audit", "archive-events", new(1, 0, 0, 1), Metadata("archive-events/subscriptions/audit"));
        ServiceBusEntityNode queue = new(EntityKind.Queue, "order-archive", null, new(1, 0, 0, 1), Metadata("order-archive"));
        return new(
            new[] { topic, subscription, archiveTopic, archiveSubscription, queue }.Select(entity => new EntityObservation(entity,
                new(new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);
    }

    private static EntityMetadata Metadata(string path) => new(path, "Active", null, null, null, null, null, null, null);

    private static TreeViewItem FindTreeItem(ItemsControl parent, EntityNode target)
    {
        parent.UpdateLayout();
        for (int index = 0; index < parent.Items.Count; index++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(index) is not TreeViewItem item) continue;
            if (ReferenceEquals(item.DataContext, target)) return item;
            TreeViewItem? nested = FindTreeItem(item, target, out bool found);
            if (found) return nested!;
        }
        throw new Xunit.Sdk.XunitException($"The namespace item '{target.Path}' was not realized.");
    }

    private static TreeViewItem? FindTreeItem(TreeViewItem parent, EntityNode target, out bool found)
    {
        parent.IsExpanded = true;
        parent.UpdateLayout();
        for (int index = 0; index < parent.Items.Count; index++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(index) is not TreeViewItem item) continue;
            if (ReferenceEquals(item.DataContext, target))
            {
                found = true;
                return item;
            }
            TreeViewItem? nested = FindTreeItem(item, target, out found);
            if (found) return nested;
        }
        found = false;
        return null;
    }

    private static void SelectTreeScope(TreeView tree, EntityNode scope)
    {
        TreeViewItem item = FindTreeItem(tree, scope);
        item.Focus();
        item.IsSelected = true;
        Drain();
        Assert.Same(scope, item.DataContext);
        Assert.True(item.IsSelected);
    }

    private static DataGridColumn[] VisibleDataColumns(Window window, DataGrid grid) => grid.Columns
        .Where(column => column.Visibility == Visibility.Visible
            && column != grid.Columns[0]
            && column != (DataGridColumn)window.FindName("StateColumn")!)
        .OrderBy(column => column.DisplayIndex)
        .ToArray();

    private static DataGridRow Row(DataGrid grid, int index)
    {
        grid.ScrollIntoView(grid.Items[index]);
        grid.UpdateLayout();
        return grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow
            ?? throw new Xunit.Sdk.XunitException("The message row was not realized.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void CaptureIfRequested(Window window, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_SCOPE_PROOF");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var content = (FrameworkElement)window.Content;
        ServiceBusEmulatorExplorer.ReadmeScreenshot.WpfScreenshot.SaveWindowContent(window,
            Path.Combine(directory, fileName), (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
    }

    private static void Complete(Task task)
    {
        Wait(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "The Investigation scope render proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Wait(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The Investigation scope did not reach its expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class FakeStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(initial));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => null!;
        public ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser(params EntityDiscoverySnapshot[] snapshots) : IInvestigationEntityBrowser
    {
        private int index;
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshots[Math.Min(index++, snapshots.Length - 1)]);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            if (bucket != MessageBucket.Active) return Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
            ExplorerMessage message = new("billing-1", 1, "{}", "{}", 2, DateTimeOffset.UtcNow, null, 0,
                "application/json", "order-1", null, "OrderCreated", new Dictionary<string, object?>(), new Dictionary<string, object?>());
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>([message]);
        }
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
