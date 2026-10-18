using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

// Refresh used to rebuild the namespace tree, so WPF selected the topic above the open
// subscription: the list reloaded and the Columns popup closed on every refresh.
[Collection("WPF presentation")]
public sealed class NamespaceTreeRefreshTests
{
    [Theory]
    [Trait("TestCategory", "UiRender")]
    [InlineData(true)]
    [InlineData(false)]
    public void Refresh_keeps_the_open_subscription_its_messages_and_the_columns_popup(bool treeFocused) => OnSta(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var messages = new FakeMessages();
        var clock = new ManualClock();
        messages.Set(new EntityAddress(EntityKind.Subscription, "store", "inv"), 1, 2);
        messages.Set(new EntityAddress(EntityKind.Subscription, "wh", "inv"), 3, 4);
        var workspace = new InvestigationWorkspace(new Store(),
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => messages), timeProvider: clock);
        var window = new InvestigationWindow(workspace);
        window.Show();
        try
        {
            Complete(workspace.ConnectAsync());
            Complete(workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id,
                [new(WatchScopeResolver.SubscriptionScopeKey("inv", "store"), true, true)]));
            var tree = (TreeView)window.FindName("NamespaceTree");
            var item = TreeItems(tree).Single(candidate => candidate.DataContext is EntityNode { Name: "store" });
            if (treeFocused) item.Focus();
            item.IsSelected = true;
            Wait(() => workspace.Browse.SelectedEntity?.Name == "store" && !workspace.Browse.IsBusy);
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("DeadLetterTab")).IsChecked = true;
            Wait(() => workspace.Browse.IsDeadLetter && workspace.Browse.Messages.Count == 2 && !workspace.Browse.IsBusy);
            EntityNode store = workspace.Browse.SelectedEntity!;
            var treeSelections = new List<object?>();
            tree.SelectedItemChanged += (_, e) => treeSelections.Add(e.NewValue);

            var popup = (System.Windows.Controls.Primitives.Popup)window.FindName("ColumnsPopup");
            popup.StaysOpen = true; // headless runs lose focus; only code-driven closes matter here
            AutomationPress.Press((Button)window.FindName("ColumnsButton"));
            Wait(() => popup.IsOpen);

            for (int tick = 0; tick < 2; tick++)
            {
                int peeks = messages.Calls;
                clock.Fire();
                Wait(() => messages.Calls > peeks && workspace.Browse.CurrentBackgroundRefresh.IsCompleted && !workspace.Browse.IsBusy);
            }
            workspace.Browse.ApplySnapshot(new Browser().DiscoverAsync(default).Result);
            Wait(() => false, TimeSpan.FromMilliseconds(200), throwOnTimeout: false);

            Assert.Empty(treeSelections);
            Assert.True(popup.IsOpen);
            Assert.Same(store, workspace.Browse.SelectedEntity);
            Assert.Equal([1L, 2L], workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
            Assert.NotNull(workspace.Browse.FocusedMessage);
        }
        finally
        {
            window.Close();
            Complete(workspace.DisposeAsync().AsTask());
        }
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }

    private static void Wait(Func<bool> condition, TimeSpan? timeout = null, bool throwOnTimeout = true)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > (timeout ?? TimeSpan.FromSeconds(5)))
            {
                if (throwOnTimeout) throw new TimeoutException("Did not reach expected state.");
                return;
            }
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<TreeViewItem> TreeItems(ItemsControl parent)
    {
        parent.UpdateLayout();
        foreach (object data in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(data) is not TreeViewItem item) continue;
            item.IsExpanded = true;
            yield return item;
            foreach (var child in TreeItems(item)) yield return child;
        }
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        private WorkspacePreferences value = new()
        {
            Profiles = [new("render", new ConnectionProfile("Refresh", "runtime", "admin"))],
            SelectedProfileId = "render", CloseToTray = false, AutoRefreshSeconds = 10,
            Watches = new Dictionary<string, IReadOnlyList<WatchPreference>>()
        };
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(value));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) { value = preferences; return Task.CompletedTask; }
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            EntityObservation Observation(EntityKind kind, string name, string? topic) => new(
                new ServiceBusEntityNode(kind, name, topic, new(0, 0, 0, 0),
                    new(name, "Active", null, null, null, null, null, null, null)),
                new(new(null, CountAvailability.NotSupported), new(null, CountAvailability.NotSupported), new(null, CountAvailability.NotSupported)));
            return Task.FromResult(new EntityDiscoverySnapshot(
                [Observation(EntityKind.Topic, "inv", null), Observation(EntityKind.Subscription, "store", "inv"),
                 Observation(EntityKind.Subscription, "wh", "inv")], DateTimeOffset.UtcNow, true, []));
        }
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<EntityAddress, ExplorerMessage[]> messages = [];
        public int Calls { get; private set; }
        public void Set(EntityAddress address, params long[] sequences) => messages[address] = sequences.Select(Message).ToArray();
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            Calls++;
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
        public bool SupportsRuntimeCounts => false;
        public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException();
        public ServiceBusClient RuntimeClient => throw new InvalidOperationException();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

