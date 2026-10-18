using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Characterizes Investigation window guarantees through its controls, independently of whether the window or a
/// view model owns the logic: actions run once when enabled and never when disabled, and closing drops late work.
/// </summary>
[Collection("WPF presentation")]
public sealed class InvestigationWindowContractTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Message_actions_run_once_when_enabled_and_are_refused_when_disabled() => OnSta(() =>
    {
        var messages = new HeldMessages();
        var (workspace, window) = Open(messages);
        try
        {
            Button refresh = (Button)window.FindName("RefreshButton")!;
            Assert.Throws<ElementNotEnabledException>(() => Invoke(refresh));
            Assert.Equal(0, messages.Calls);

            Connect(workspace);
            EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
            Task select = workspace.Browse.SelectAsync(queue, deadLetter: false);
            WaitUntil(() => select.IsCompleted && !workspace.Browse.IsBusy);
            int callsBeforeRefresh = messages.Calls;

            Invoke(refresh);
            WaitUntil(() => messages.Calls > callsBeforeRefresh && !workspace.Browse.IsBusy);
            PumpFor(TimeSpan.FromMilliseconds(200));
            Assert.Equal(callsBeforeRefresh + 1, messages.Calls);

            Assert.Throws<ElementNotEnabledException>(() => Invoke((Button)window.FindName("DeleteButton")!));
            Assert.Throws<ElementNotEnabledException>(() => Invoke((Button)window.FindName("LoadMoreButton")!));
            Assert.Equal(callsBeforeRefresh + 1, messages.Calls);
        }
        finally { Close(workspace, window); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Inspector_copy_puts_exactly_the_displayed_text_on_the_clipboard() => OnSta(() =>
    {
        var messages = new HeldMessages { Page = [CreateMessage("orders-1", "correlation-1", "{\"order\":1}")] };
        var (workspace, window) = Open(messages);
        string? previousClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        try
        {
            Connect(workspace);
            EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
            Task select = workspace.Browse.SelectAsync(queue, deadLetter: false);
            WaitUntil(() => select.IsCompleted && workspace.Browse.Messages.Count == 1);
            workspace.Browse.FocusedMessage = workspace.Browse.Messages.Single();
            WaitUntil(() => workspace.Inspector.Current is not null);
            Button copy = (Button)window.FindName("CopyButton")!;

            CopyAndExpect(() => Invoke(copy), workspace.Inspector.Document.Text);
            Assert.Contains("\"order\"", workspace.Inspector.Document.Text);

            var properties = (IToggleProvider)new ToggleButtonAutomationPeer((ToggleButton)window.FindName("PropertiesTab")!)
                .GetPattern(PatternInterface.Toggle)!;
            properties.Toggle();
            WaitUntil(() => ((ToggleButton)window.FindName("PropertiesTab")!).IsChecked == true);
            CopyAndExpect(() => Invoke(copy), workspace.Inspector.PropertiesText);
            CopyAndExpect(() => Invoke((Button)window.FindName("CopyCorrelationButton")!), "correlation-1");
        }
        finally
        {
            if (previousClipboard is null) Clipboard.Clear(); else Clipboard.SetText(previousClipboard);
            Close(workspace, window);
        }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Closing_the_window_drops_a_late_peek_result_and_stops_broker_work() => OnSta(() =>
    {
        var messages = new HeldMessages();
        Exception? unhandled = null;
        Dispatcher.CurrentDispatcher.UnhandledException += (_, args) => { unhandled = args.Exception; args.Handled = true; };
        var (workspace, window) = Open(messages);
        Connect(workspace);
        EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));

        messages.HoldNext = true;
        _ = workspace.Browse.SelectAsync(queue, deadLetter: false);
        WaitUntil(() => messages.Held is not null);
        window.Close();
        WaitUntil(() => !window.IsVisible);
        int callsAtClose = messages.Calls;

        messages.Held!.SetResult([CreateMessage("late-result")]);
        PumpFor(TimeSpan.FromMilliseconds(500));

        Assert.Null(unhandled);
        Assert.False(window.IsVisible);
        Assert.False(workspace.IsConnected);
        Assert.DoesNotContain(workspace.Browse.Messages, row => row.Delivery.Message.MessageId == "late-result");
        Assert.Equal(callsAtClose, messages.Calls);
    });

    private static (InvestigationWorkspace Workspace, InvestigationWindow Window) Open(HeldMessages messages)
    {
        var workspace = new InvestigationWorkspace(new Store(),
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => messages),
            timeProvider: BaselineClock.Instance);
        var window = new InvestigationWindow(workspace)
        {
            Width = 1100,
            Height = 800,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        window.Show();
        WaitUntil(() => window.IsVisible && window.ActualWidth > 0);
        return (workspace, window);
    }

    private static void Connect(InvestigationWorkspace workspace)
    {
        Task connect = workspace.ConnectAsync();
        WaitUntil(() => connect.IsCompleted);
        connect.GetAwaiter().GetResult();
    }

    /// <summary>Clicks through UI Automation, the route a user or screen reader takes; disabled buttons refuse it.</summary>
    private static void Invoke(Button button)
    {
        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!;
        invoke.Invoke();
    }

    private static void Close(InvestigationWorkspace workspace, InvestigationWindow window)
    {
        if (window.IsVisible)
        {
            window.Close();
            WaitUntil(() => !window.IsVisible);
        }

        Task dispose = workspace.DisposeAsync().AsTask();
        WaitUntil(() => dispose.IsCompleted);
    }

    /// <summary>Copies over a sentinel and waits for exactly the expected text; the clipboard is shared OS state.</summary>
    private static void CopyAndExpect(Action copy, string expected)
    {
        Assert.False(string.IsNullOrEmpty(expected));
        Clipboard.SetText("sentinel");
        copy();
        WaitUntil(() => Clipboard.GetText() == expected);
    }

    private static ExplorerMessage CreateMessage(string id, string? correlationId = null, string body = "{}") =>
        new(id, 1, body, body, body.Length, BaselineClock.Instance.GetUtcNow(), null, 0, "application/json", correlationId,
            null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private static void PumpFor(TimeSpan duration)
    {
        DateTime until = DateTime.UtcNow + duration;
        WaitUntil(() => DateTime.UtcNow >= until);
    }

    private static void WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The Investigation window did not reach the expected state.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The Investigation window contract exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        private static readonly WorkspacePreferences Preferences = new()
        {
            Profiles = [new InvestigationProfile("lifetime", new ConnectionProfile("Lifetime proof", "runtime", "admin"))],
            SelectedProfileId = "lifetime",
            WindowWidth = 1100,
            WindowHeight = 800
        };

        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(Preferences));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Factory : IServiceBusClientFactory
    {
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            var queue = new ServiceBusEntityNode(EntityKind.Queue, "orders", null, new EntityRuntimeCounts(1, 0, 0, 1),
                new EntityMetadata("orders", "Active", null, null, null, null, null, null, null));
            var observation = new EntityObservation(queue, new EntityCountObservation(
                new(1, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known)));
            return Task.FromResult(new EntityDiscoverySnapshot([observation], BaselineClock.Instance.GetUtcNow(), true, []));
        }
    }

    /// <summary>Holds one peek open and, like a slow broker, ignores cancellation until released.</summary>
    private sealed class HeldMessages : IServiceBusMessageService
    {
        public bool HoldNext { get; set; }
        public IReadOnlyList<ExplorerMessage> Page { get; init; } = [];
        public TaskCompletionSource<IReadOnlyList<ExplorerMessage>>? Held { get; private set; }
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            if (!HoldNext) return Task.FromResult(bucket == MessageBucket.Active ? Page : (IReadOnlyList<ExplorerMessage>)[]);
            HoldNext = false;
            Held = new TaskCompletionSource<IReadOnlyList<ExplorerMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return Held.Task;
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
