using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class NamespaceIndicatorTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void WatchIndicatorsResolveInheritedRulesExclusionsAndSurviveDiscoveryRefresh() => OnSta(() =>
    {
        using var fixture = new Fixture();
        var rules = new WatchPreference[]
        {
            new(WatchScopeResolver.ConnectionScopeKey, true, true),
            new(WatchScopeResolver.QueueScopeKey("orders-in"), false, null),
            new(WatchScopeResolver.SubscriptionScopeKey("orders", "analytics"), null, null, false)
        };
        Complete(fixture.Workspace.UpdateWatchRulesAsync(fixture.Workspace.SelectedProfile.Id, rules));

        EntityNode queue = fixture.Entity("orders-in");
        EntityNode billing = fixture.Entity("billing");
        EntityNode analytics = fixture.Entity("analytics");
        Assert.False(queue.IsActiveWatched);
        Assert.True(queue.IsDlqWatched);
        Assert.True(billing.IsActiveWatched);
        Assert.True(billing.IsDlqWatched);
        Assert.False(analytics.IsActiveWatched);
        Assert.False(analytics.IsDlqWatched);
        Assert.Contains("Watch", billing.ActiveCountDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch", billing.DlqCountDetail, StringComparison.OrdinalIgnoreCase);

        fixture.Browser.Snapshot = CreateSnapshot(activeCount: 17, deadLetterCount: 6);
        Complete(fixture.Workspace.Browse.RefreshAsync());

        EntityNode refreshedBilling = fixture.Entity("billing");
        Assert.Equal("17", refreshedBilling.DisplayMessageCount);
        Assert.True(refreshedBilling.IsActiveWatched);
        Assert.True(refreshedBilling.IsDlqWatched);
        Assert.False(fixture.Entity("analytics").IsActiveWatched);
        Assert.False(fixture.Entity("analytics").IsDlqWatched);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void RefreshIndicatorTracksCurrentEntityBucketIntervalAndPauseIndependentlyOfWatch() => OnSta(() =>
    {
        using var fixture = new Fixture();
        Complete(fixture.Workspace.UpdateWatchRulesAsync(fixture.Workspace.SelectedProfile.Id,
            [new(WatchScopeResolver.ConnectionScopeKey, true, false)]));

        EntityNode queue = fixture.Entity("orders-in");
        EntityNode subscription = fixture.Entity("billing");
        Assert.Equal("Running", queue.RefreshIndicator);
        Assert.Contains("Active", queue.RefreshDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("None", subscription.RefreshIndicator);

        string activeDetail = queue.RefreshDetail;
        var deadLetterTab = (ToggleButton)fixture.Window.FindName("DeadLetterTab");
        Set(deadLetterTab, true);
        Wait(() => fixture.Workspace.Browse.IsDeadLetter && !fixture.Workspace.Browse.IsBusy);
        Assert.Equal("Running", queue.RefreshIndicator);
        Assert.NotEqual(activeDetail, queue.RefreshDetail);

        Complete(fixture.Workspace.Browse.SelectAsync(subscription, deadLetter: true));
        Assert.Equal("None", queue.RefreshIndicator);
        Assert.Equal("Running", subscription.RefreshIndicator);
        Assert.True(subscription.IsActiveWatched);
        Assert.False(subscription.IsDlqWatched);

        var pause = (Button)fixture.Window.FindName("PauseButton");
        Invoke(pause);
        Wait(() => subscription.RefreshIndicator == "Paused");
        Assert.True(subscription.IsActiveWatched);
        Assert.Contains("30 seconds", subscription.RefreshDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Resume automatic refresh", System.Windows.Automation.AutomationProperties.GetName(pause));

        var interval = (ComboBox)fixture.Window.FindName("AutoInterval");
        interval.SelectedIndex = 0;
        Wait(() => subscription.RefreshIndicator == "None" && queue.RefreshIndicator == "None");
        Invoke(pause);
        Assert.False(fixture.Timer.IsEnabled);
        interval.SelectedIndex = 3;
        Wait(() => fixture.Timer.IsEnabled && subscription.RefreshIndicator == "Running");
        Assert.Equal("Running", subscription.RefreshIndicator);
        Assert.True(subscription.IsActiveWatched);
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void RefreshIndicatorIsHiddenDuringSearchWorkbenchAndDisconnect() => OnSta(() =>
    {
        using var fixture = new Fixture();
        Complete(fixture.Workspace.Search.StartAsync("billing"));
        Assert.True(fixture.Workspace.Search.IsActive);
        Assert.All(Entities(fixture.Workspace.Search.Roots), entity => Assert.Equal("None", entity.RefreshIndicator));

        fixture.Workspace.Search.Clear();
        Wait(() => !fixture.Workspace.Search.IsActive && fixture.Workspace.Browse.SelectedEntity is not null);
        Assert.Equal("Running", fixture.Workspace.Browse.SelectedEntity!.RefreshIndicator);

        ((ToggleButton)fixture.Window.FindName("MessageLibraryTab")).IsChecked = true;
        Wait(() => fixture.Window.FindName("MessageLibraryPrototype") is FrameworkElement { Visibility: Visibility.Visible });
        Assert.All(Entities(fixture.Workspace.Browse.Roots), entity => Assert.Equal("None", entity.RefreshIndicator));

        ((ToggleButton)fixture.Window.FindName("InvestigationWorkspaceTab")).IsChecked = true;
        Complete(fixture.Workspace.DisconnectAsync());
        Assert.All(Entities(fixture.Workspace.Browse.Roots), entity => Assert.Equal("None", entity.RefreshIndicator));
        ((ToggleButton)fixture.Window.FindName("MessageLibraryTab")).IsChecked = true;
        Wait(() => fixture.Window.FindName("MessageLibraryPrototype") is FrameworkElement { Visibility: Visibility.Visible });
        var tree = (TreeView)fixture.Window.FindName("NamespaceTree");
        Assert.All(Entities(tree.ItemsSource.Cast<EntityNode>()), entity => Assert.Equal("None", entity.RefreshIndicator));
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
            Complete(Workspace.Browse.SelectAsync(Entity("orders-in"), deadLetter: false));
            Assert.True(Workspace.IsConnected);
            Assert.True(Timer.IsEnabled);
        }

        public EntityNode Entity(string name) => Workspace.Browse.AllEntities().Single(node => node.Name == name);

        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Window.Close();
                Wait(() => !Window.IsVisible);
            }
            Complete(Workspace.DisposeAsync().AsTask());
        }
    }

    private static EntityDiscoverySnapshot CreateSnapshot(long activeCount = 3, long deadLetterCount = 2)
    {
        ServiceBusEntityNode queue = Entity(EntityKind.Queue, "orders-in", null, activeCount, deadLetterCount);
        ServiceBusEntityNode topic = Entity(EntityKind.Topic, "orders", null, 0, 0);
        ServiceBusEntityNode billing = Entity(EntityKind.Subscription, "billing", "orders", activeCount, deadLetterCount);
        ServiceBusEntityNode analytics = Entity(EntityKind.Subscription, "analytics", "orders", 4, 1);
        EntityObservation[] observations = new[] { queue, topic, billing, analytics }.Select(entity => new EntityObservation(
            entity,
            new(new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray();
        return new(observations, DateTimeOffset.UtcNow, true, []);
    }

    private static ServiceBusEntityNode Entity(EntityKind kind, string name, string? topicName, long active, long deadLetter) =>
        new(kind, name, topicName, new(active, deadLetter, 0, active + deadLetter),
            new(name, "Active", null, null, null, null, null, null, null));

    private static IEnumerable<EntityNode> Entities(IEnumerable<EntityNode> roots) => roots
        .SelectMany(WatchRuleEditor.Flatten)
        .Where(entity => entity.Kind is nameof(EntityKind.Queue) or nameof(EntityKind.Topic) or nameof(EntityKind.Subscription));

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Namespace indicator UI proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }

    private static void Wait(Func<bool> condition, TimeSpan? timeout = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > (timeout ?? TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Namespace indicator did not reach the expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
        .GetPattern(PatternInterface.Invoke)).Invoke();

    private static void Set(ToggleButton button, bool value)
    {
        if (button.IsChecked == value) return;
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
            .GetPattern(PatternInterface.Toggle)).Toggle();
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        private WorkspacePreferences value = new()
        {
            Profiles = [new("indicators", new ConnectionProfile("Indicator proof", "runtime", "admin"))],
            SelectedProfileId = "indicators", CloseToTray = false, AutoRefreshSeconds = 30,
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>()
        };
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(value));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        { value = preferences; return Task.CompletedTask; }
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public EntityDiscoverySnapshot Snapshot { get; set; } = CreateSnapshot();
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
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
