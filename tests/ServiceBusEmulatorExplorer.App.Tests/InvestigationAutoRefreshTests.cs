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
    public void DisconnectedWorkbenchUsesLabeledSampleNodesOutsideLiveBrowseRoots() => OnSta(() =>
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
        typeof(InvestigationWindow).GetMethod("ApplyWorkbenchTemplateAssociations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Window, [new[] { "inventory-events" }]);
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
        Assert.Equal("Send to: Choose destination", ((TextBlock)view.FindName("DestinationText")).Text);

        Complete(fixture.Workspace.DisconnectAsync());
        Assert.Contains("Sample entities", ((TextBlock)fixture.Window.FindName("NamespaceModeLabel")).Text);
        Assert.Equal(2, tree.Items.Count);
        Assert.True(((EntityNode)tree.Items[0]).IsVisible);
    });

    private sealed class Fixture : IDisposable
    {
        public Browser Browser { get; } = new();
        public InvestigationWorkspace Workspace { get; }
        public InvestigationWindow Window { get; }
        public DispatcherTimer Timer => (DispatcherTimer)typeof(InvestigationWindow)
            .GetField("refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        public Fixture()
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Workspace = new InvestigationWorkspace(new Store(),
                new BrokerConnectionWorkflow(() => new Factory(), _ => Browser, _ => new Messages()));
            Window = new InvestigationWindow(Workspace);
            Window.Show();
            Complete(Workspace.ConnectAsync());
            Complete(Workspace.Browse.SelectAsync(Workspace.Browse.AllEntities().Single(), false));
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
        public int Discoveries { get; private set; }
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        { Discoveries++; return Task.FromResult(Snapshot()); }
        public static EntityDiscoverySnapshot Snapshot(long activeCount = 0) => new([new EntityObservation(
            new ServiceBusEntityNode(EntityKind.Queue, "orders", null, new(0, 0, 0, 0),
                new("orders", "Active", null, null, null, null, null, null, null)),
            new(new(activeCount, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known)))],
            DateTimeOffset.UtcNow, true, []);
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
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
