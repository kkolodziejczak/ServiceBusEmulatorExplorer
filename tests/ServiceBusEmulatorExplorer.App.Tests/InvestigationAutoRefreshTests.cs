using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationAutoRefreshTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void ThirtySecondRefreshStillRunsWhileWatchDiscoveryUpdatesTheBrowseTree() => OnSta(() =>
    {
        using var fixture = new Fixture();
        int initialDiscoveries = fixture.Browser.Discoveries;
        int appliedSnapshots = 0;
        bool eligibleThroughout = true;
        var watchUpdates = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        watchUpdates.Tick += (_, _) =>
        {
            // This is the public Browse operation performed by the workspace's Watch discovery callback.
            fixture.Workspace.Browse.ApplySnapshot(Browser.Snapshot());
            appliedSnapshots++;
            eligibleThroughout &= fixture.Timer.IsEnabled && fixture.Timer.Interval == TimeSpan.FromSeconds(30)
                && !fixture.Workspace.Browse.IsBusy && !fixture.Workspace.Search.IsActive;
        };
        var elapsed = Stopwatch.StartNew();
        watchUpdates.Start();
        try
        {
            Wait(() => fixture.Browser.Discoveries > initialDiscoveries || elapsed.Elapsed >= TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(36));
            Assert.True(appliedSnapshots >= 20, "Repeated Watch discovery updates must overlap the actual refresh interval.");
            Assert.True(eligibleThroughout, "The enabled 30-second refresh must remain eligible while the dispatcher applies discovery updates.");
            Assert.True(fixture.Browser.Discoveries > initialDiscoveries,
                $"No automatic broker discovery occurred in {elapsed.Elapsed.TotalSeconds:F1}s despite {appliedSnapshots} dispatcher updates and an enabled, idle 30-second timer.");
            Assert.InRange(elapsed.Elapsed.TotalSeconds, 28, 35);
            Assert.Equal(initialDiscoveries + 1, fixture.Browser.Discoveries);
        }
        finally { watchUpdates.Stop(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void PauseAndIntervalControlsStopResumeAndChangeTheTimer() => OnSta(() =>
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Timer.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Timer.Interval);
        var pause = (Button)fixture.Window.FindName("PauseButton");
        Invoke(pause);
        Wait(() => !fixture.Timer.IsEnabled);
        Assert.Equal("Resume automatic refresh", AutomationProperties.GetName(pause));
        fixture.Workspace.Browse.ApplySnapshot(Browser.Snapshot());
        Assert.False(fixture.Timer.IsEnabled);
        Invoke(pause);
        Wait(() => fixture.Timer.IsEnabled);
        Assert.Equal("Pause automatic refresh", AutomationProperties.GetName(pause));

        var interval = (ComboBox)fixture.Window.FindName("AutoInterval");
        interval.SelectedIndex = 1;
        Wait(() => fixture.Timer.Interval == TimeSpan.FromSeconds(5));
        Assert.Equal(5, fixture.Workspace.Preferences.AutoRefreshSeconds);
        interval.SelectedIndex = 0;
        Wait(() => !fixture.Timer.IsEnabled);
        fixture.Workspace.Browse.ApplySnapshot(Browser.Snapshot());
        Assert.False(fixture.Timer.IsEnabled);
        interval.SelectedIndex = 3;
        Wait(() => fixture.Timer.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Timer.Interval);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void SearchAndDisconnectSuspendRefreshUntilBrowseAndConnectionResume() => OnSta(() =>
    {
        using var fixture = new Fixture();
        Complete(fixture.Workspace.Search.StartAsync("checkout"));
        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.False(fixture.Timer.IsEnabled);
        fixture.Workspace.Browse.ApplySnapshot(Browser.Snapshot());
        Assert.False(fixture.Timer.IsEnabled);
        Invoke((Button)fixture.Window.FindName("ClearSearchButton"));
        Wait(() => !fixture.Workspace.Search.IsActive && fixture.Timer.IsEnabled);
        Complete(fixture.Workspace.DisconnectAsync());
        Assert.False(fixture.Timer.IsEnabled);
        Complete(fixture.Workspace.ConnectAsync());
        Assert.True(fixture.Timer.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Timer.Interval);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void WorkbenchUsesLiveBrowseTreeAndRefreshesWhileInvestigationSearchIsPreserved() => OnSta(() =>
    {
        using var fixture = new Fixture();
        var tree = (TreeView)fixture.Window.FindName("NamespaceTree");
        Assert.Null(fixture.Window.FindName("PrototypeNamespaceTree"));
        ((TextBox)fixture.Window.FindName("SearchBox")).Text = "checkout";
        Complete(fixture.Workspace.Search.StartAsync("checkout"));
        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.False(fixture.Timer.IsEnabled);

        int discoveries = fixture.Browser.Discoveries;
        OpenWorkbench(fixture.Window);
        Assert.Same(fixture.Workspace.Browse.Roots, tree.ItemsSource);
        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.True(fixture.Timer.IsEnabled);
        Assert.Equal("", ((TextBox)fixture.Window.FindName("SearchBox")).Text);
        fixture.Workspace.Browse.ApplySnapshot(Browser.Snapshot(42));
        Assert.Equal("42", fixture.Workspace.Browse.AllEntities().Single().DisplayMessageCount);

        typeof(InvestigationWindow).GetMethod("RefreshTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Window, [null, EventArgs.Empty]);
        Wait(() => fixture.Browser.Discoveries > discoveries);

        ((System.Windows.Controls.Primitives.ToggleButton)fixture.Window.FindName("InvestigationWorkspaceTab")).IsChecked = true;
        Assert.Same(fixture.Workspace.Search.Roots, tree.ItemsSource);
        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.Equal("checkout", ((TextBox)fixture.Window.FindName("SearchBox")).Text);
        Assert.False(fixture.Timer.IsEnabled);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void WatchedMessagesRefreshInSearchAndWorkbenchWithoutChangingSelectionOrFocus() => OnSta(() =>
    {
        using var fixture = new Fixture(includeSecondQueue: true);
        EntityNode watched = fixture.Workspace.Browse.AllEntities().Single(node => node.Name == "orders");
        EntityNode selected = fixture.Workspace.Browse.AllEntities().Single(node => node.Name == "invoices");
        Complete(fixture.Workspace.UpdateWatchRulesAsync(fixture.Workspace.SelectedProfile.Id,
            [new(WatchScopeResolver.QueueScopeKey("orders"), true, false)]));
        Complete(fixture.Workspace.Browse.SelectAsync(selected, deadLetter: false));
        fixture.Workspace.Browse.FocusedMessage = fixture.Workspace.Browse.Messages.Single();
        var focused = fixture.Workspace.Browse.FocusedMessage;

        fixture.Messages.Set(new(EntityKind.Queue, "orders"), 1, 2);
        Complete(fixture.Workspace.Search.StartAsync("invoices"));
        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.True(fixture.Timer.IsEnabled, "Watch must keep the shared timer active during Investigation search.");
        int searchPeeks = fixture.Messages.CallsFor(new(EntityKind.Queue, "orders"));
        TickRefresh(fixture.Window);
        Wait(() => fixture.Messages.CallsFor(new(EntityKind.Queue, "orders")) > searchPeeks);
        Assert.Equal("invoices", fixture.Workspace.Browse.SelectedEntity?.Name);
        Assert.Same(focused, fixture.Workspace.Browse.FocusedMessage);

        fixture.Messages.Set(new(EntityKind.Queue, "orders"), 1, 2, 3);
        OpenWorkbench(fixture.Window);
        Assert.True(fixture.Timer.IsEnabled, "Watch must keep the shared timer active in Message Workbench.");
        int workbenchPeeks = fixture.Messages.CallsFor(new(EntityKind.Queue, "orders"));
        TickRefresh(fixture.Window);
        Wait(() => fixture.Messages.CallsFor(new(EntityKind.Queue, "orders")) > workbenchPeeks);
        Assert.Equal("invoices", fixture.Workspace.Browse.SelectedEntity?.Name);
        Assert.Same(focused, fixture.Workspace.Browse.FocusedMessage);

        ((System.Windows.Controls.Primitives.ToggleButton)fixture.Window.FindName("InvestigationWorkspaceTab")).IsChecked = true;
        Invoke((Button)fixture.Window.FindName("ClearSearchButton"));
        Wait(() => !fixture.Workspace.Search.IsActive);
        Complete(fixture.Workspace.Browse.SelectAsync(watched, deadLetter: false));
        Assert.Equal(new long[] { 1, 2, 3 }, fixture.Workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
        fixture.Workspace.Browse.FocusedMessage = fixture.Workspace.Browse.Messages.First();
        var watchedFocus = fixture.Workspace.Browse.FocusedMessage;

        OpenWorkbench(fixture.Window);
        fixture.Messages.Set(new(EntityKind.Queue, "orders"), 1, 2, 3, 4);
        int beforeWorkbenchReturn = fixture.Messages.CallsFor(new(EntityKind.Queue, "orders"));
        TickRefresh(fixture.Window);
        Wait(() => fixture.Messages.CallsFor(new(EntityKind.Queue, "orders")) > beforeWorkbenchReturn
            && BackgroundRefresh(fixture.Workspace).IsCompleted);
        Assert.Equal(new long[] { 1, 2, 3 }, fixture.Workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Same(watchedFocus, fixture.Workspace.Browse.FocusedMessage);

        ((System.Windows.Controls.Primitives.ToggleButton)fixture.Window.FindName("InvestigationWorkspaceTab")).IsChecked = true;
        Assert.Equal(new long[] { 1, 2, 3, 4 }, fixture.Workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Same(watchedFocus, fixture.Workspace.Browse.FocusedMessage);

        fixture.Messages.Set(new(EntityKind.Queue, "orders"), 1, 2, 3, 4, 5);
        Complete(fixture.Workspace.Search.StartAsync("orders"));
        int beforeSearchClear = fixture.Messages.CallsFor(new(EntityKind.Queue, "orders"));
        TickRefresh(fixture.Window);
        Wait(() => fixture.Messages.CallsFor(new(EntityKind.Queue, "orders")) > beforeSearchClear
            && BackgroundRefresh(fixture.Workspace).IsCompleted);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, fixture.Workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Same(watchedFocus, fixture.Workspace.Browse.FocusedMessage);

        Invoke((Button)fixture.Window.FindName("ClearSearchButton"));
        Wait(() => !fixture.Workspace.Search.IsActive);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, fixture.Workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Same(watchedFocus, fixture.Workspace.Browse.FocusedMessage);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void SelectingSingleDestinationTemplateFiltersDisconnectedNamespaceSample() => OnSta(() =>
    {
        using var fixture = new Fixture();
        Complete(fixture.Workspace.DisconnectAsync());
        Assert.Empty(fixture.Workspace.Browse.Roots);
        var tree = (TreeView)fixture.Window.FindName("NamespaceTree");
        OpenWorkbench(fixture.Window);

        Assert.NotSame(fixture.Workspace.Browse.Roots, tree.ItemsSource);
        Assert.Equal(2, tree.Items.Count);
        Assert.Contains("Sample entities", ((TextBlock)fixture.Window.FindName("NamespaceModeLabel")).Text);
        var sampleQueue = ((EntityNode)tree.Items[0]).Children.Single();
        Assert.Equal("order-replies", sampleQueue.Name);
        Assert.Equal("", sampleQueue.DisplayMessageCount);
        Assert.Empty(fixture.Workspace.Browse.Roots);
        var workbench = (MessageLibraryPrototypeView)fixture.Window.FindName("MessageLibraryPrototype");
        var libraryTree = (TreeView)workbench.FindName("LibraryTree");
        var template = TreeItems(libraryTree)
            .Single(item => Equals(item.Tag, "template:Stock reserved"));
        template.IsSelected = true;
        Assert.Equal("inventory-events", ((TextBox)fixture.Window.FindName("SearchBox")).Text);
        var sampleTopic = ((EntityNode)tree.Items[1]).Children.Single(node => node.Name == "inventory-events");
        Assert.False(((EntityNode)tree.Items[0]).IsVisible);
        Assert.True(sampleTopic.IsVisible);

        ((System.Windows.Controls.Primitives.ToggleButton)fixture.Window.FindName("InvestigationWorkspaceTab")).IsChecked = true;
        Assert.Same(fixture.Workspace.Surface.Roots, tree.ItemsSource);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void WorkbenchEntitySelectionFiltersNamespaceWithoutRetargetingAnUnrelatedTemplate() => OnSta(() =>
    {
        using var fixture = new Fixture();
        OpenWorkbench(fixture.Window);
        var tree = (TreeView)fixture.Window.FindName("NamespaceTree");
        var entity = fixture.Workspace.Browse.AllEntities().Single();
        var args = new RoutedPropertyChangedEventArgs<object>(new object(), entity, TreeView.SelectedItemChangedEvent);
        typeof(InvestigationWindow).GetMethod("Tree_Selected", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Window, [tree, args]);

        var view = (MessageLibraryPrototypeView)fixture.Window.FindName("MessageLibraryPrototype");
        Assert.Equal("orders", ((TextBox)fixture.Window.FindName("SearchBox")).Text);
        Assert.Equal(Visibility.Visible, ((Border)view.FindName("PrepareDestinationWarning")).Visibility);
        Assert.Contains("Topic \"order-events\" is unavailable",
            ((TextBlock)view.FindName("PrepareDestinationWarningText")).Text, StringComparison.Ordinal);
        Assert.Equal("order-events", ((System.Windows.Controls.ComboBox)view.FindName("TemplateDestination")).SelectedValue);
        Assert.False(((System.Windows.Controls.Button)view.FindName("ReviewButton")).IsEnabled);

        Complete(fixture.Workspace.DisconnectAsync());
        Assert.Contains("Sample entities", ((TextBlock)fixture.Window.FindName("NamespaceModeLabel")).Text);
        Assert.Equal(2, tree.Items.Count);
        Assert.True(((EntityNode)tree.Items[0]).IsVisible);
        Assert.Equal(Visibility.Visible, ((Border)view.FindName("PrepareDestinationWarning")).Visibility);
        Assert.Contains("Topic \"order-events\" is not verified",
            ((TextBlock)view.FindName("PrepareDestinationWarningText")).Text, StringComparison.Ordinal);
    });

    private sealed class Fixture : IDisposable
    {
        public Browser Browser { get; }
        public Messages Messages { get; } = new();
        public InvestigationWorkspace Workspace { get; }
        public InvestigationWindow Window { get; }
        public DispatcherTimer Timer => (DispatcherTimer)typeof(InvestigationWindow)
            .GetField("refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        public Fixture(bool includeSecondQueue = false)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Browser = new Browser(includeSecondQueue);
            Messages.Set(new(EntityKind.Queue, "orders"), 1);
            Messages.Set(new(EntityKind.Queue, "invoices"), 10);
            Workspace = new InvestigationWorkspace(new Store(),
                new BrokerConnectionWorkflow(() => new Factory(), _ => Browser, _ => Messages));
            Window = new InvestigationWindow(Workspace);
            Window.Show();
            Complete(Workspace.ConnectAsync());
            Complete(Workspace.Browse.SelectAsync(Workspace.Browse.AllEntities().Single(node => node.Name == "orders"), false));
            Assert.True(Workspace.IsConnected);
            Assert.True(Timer.IsEnabled);
        }
        public void Dispose()
        {
            Window.Close();
            Wait(() => !Window.IsVisible);
            Complete(Workspace.DisposeAsync().AsTask());
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Automatic refresh proof exceeded 45 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> condition, TimeSpan? timeout = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > (timeout ?? TimeSpan.FromSeconds(5))) throw new TimeoutException("Automatic refresh did not reach the expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
        .GetPattern(PatternInterface.Invoke)).Invoke();

    private static void OpenWorkbench(InvestigationWindow window) =>
        ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("MessageLibraryTab")).IsChecked = true;

    private static void TickRefresh(InvestigationWindow window) =>
        typeof(InvestigationWindow).GetMethod("RefreshTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null, EventArgs.Empty]);

    private static Task BackgroundRefresh(InvestigationWorkspace workspace) =>
        (Task)typeof(MessageBrowseWorkflow).GetField("backgroundRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(workspace.Browse)!;

    private static IEnumerable<TreeViewItem> TreeItems(TreeView tree)
    {
        foreach (TreeViewItem root in tree.Items.OfType<TreeViewItem>())
        {
            yield return root;
            foreach (var child in TreeItems(root)) yield return child;
        }
    }

    private static IEnumerable<TreeViewItem> TreeItems(TreeViewItem parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (var descendant in TreeItems(child)) yield return descendant;
        }
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        private WorkspacePreferences value = new()
        {
            Profiles = [new("render", new ConnectionProfile("Refresh proof", "runtime", "admin"))],
            SelectedProfileId = "render", CloseToTray = false, AutoRefreshSeconds = 30,
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>()
        };
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(value));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) { value = preferences; return Task.CompletedTask; }
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        private readonly bool includeSecondQueue;
        public int Discoveries { get; private set; }
        public Browser(bool includeSecondQueue) => this.includeSecondQueue = includeSecondQueue;
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        { Discoveries++; return Task.FromResult(Snapshot(includeSecondQueue: includeSecondQueue)); }
        public static EntityDiscoverySnapshot Snapshot(long activeCount = 0, bool includeSecondQueue = false)
        {
            EntityObservation Observation(string name, long count) => new(
                new ServiceBusEntityNode(EntityKind.Queue, name, null, new(0, 0, 0, 0),
                    new(name, "Active", null, null, null, null, null, null, null)),
                new(new(count, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known)));
            var entities = new List<EntityObservation> { Observation("orders", activeCount) };
            if (includeSecondQueue) entities.Add(Observation("invoices", 0));
            return new(entities, DateTimeOffset.UtcNow, true, []);
        }
    }

    private sealed class Messages : IServiceBusMessageService
    {
        private readonly ConcurrentDictionary<EntityAddress, IReadOnlyList<ExplorerMessage>> messages = new();
        private readonly ConcurrentQueue<EntityAddress> requests = new();
        public int CallsFor(EntityAddress address) => requests.Count(item => item == address);
        public void Set(EntityAddress address, params long[] sequences)
            => messages[address] = sequences.Select(Message).ToArray();
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            requests.Enqueue(address);
            IReadOnlyList<ExplorerMessage> result = messages.TryGetValue(address, out var source)
                ? source.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value).Take(take).ToArray()
                : [];
            return Task.FromResult(result);
        }
        private static ExplorerMessage Message(long sequence) => new($"message-{sequence}", sequence,
            $"body-{sequence}", $"body-{sequence}", 7, null, null, 0, null, null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class Factory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException();
        public ServiceBusClient RuntimeClient => throw new InvalidOperationException();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
