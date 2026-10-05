using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
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
public sealed class InvestigationDeleteSelectionRenderTests
{
    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Delete_requires_an_explicit_dlq_checkbox_at_supported_window_sizes(int width, int height) => OnSta(() =>
    {
        var fixture = new Fixture(width, height);
        try
        {
            MessageRow dlq = fixture.Workspace.Browse.Messages.Single();
            MessageRow active = CreateRow(MessageBucket.Active, 2);
            fixture.Workspace.Browse.Messages.Add(active);
            Drain();

            Focus(fixture, dlq);
            Assert.True(((Button)fixture.Window.FindName("ReplayButton")).IsEnabled);
            Assert.Equal(0, fixture.Workspace.Surface.SelectedCount);
            Assert.False(fixture.Delete.IsEnabled);
            Capture(fixture.Window, width, height, "disabled-" + fixture.Workspace.Surface.SelectedCount);
            Assert.Same(dlq.Delivery, fixture.Workspace.Inspector.Current);

            ToggleCheckbox(fixture, dlq);
            Assert.Equal(1, fixture.Workspace.Surface.SelectedCount);
            Assert.True(dlq.IsSelected);
            Assert.True(fixture.Delete.IsEnabled);
            Capture(fixture.Window, width, height, "checked");
            Assert.Same(dlq.Delivery, fixture.Workspace.Inspector.Current);

            ToggleCheckbox(fixture, dlq);
            Assert.Equal(0, fixture.Workspace.Surface.SelectedCount);
            Assert.False(fixture.Delete.IsEnabled);
            Capture(fixture.Window, width, height, "disabled-" + fixture.Workspace.Surface.SelectedCount);
            Assert.Same(dlq.Delivery, fixture.Workspace.Inspector.Current);

            ToggleCheckbox(fixture, active);
            Assert.Equal(1, fixture.Workspace.Surface.SelectedCount);
            Assert.True(active.IsSelected);
            Assert.False(fixture.Delete.IsEnabled);
            Capture(fixture.Window, width, height, "disabled-" + fixture.Workspace.Surface.SelectedCount);

            ToggleCheckbox(fixture, dlq);
            Assert.Equal(2, fixture.Workspace.Surface.SelectedCount);
            Assert.False(fixture.Delete.IsEnabled);
            Capture(fixture.Window, width, height, "disabled-" + fixture.Workspace.Surface.SelectedCount);
            Assert.Same(dlq.Delivery, fixture.Workspace.Inspector.Current);
        }
        finally { fixture.Dispose(); }
    });

    private static void Capture(Window window, int width, int height, string state)
    {
        string? directory = Environment.GetEnvironmentVariable("SBE_DELETE_SELECTION_PROOF");
        if (string.IsNullOrWhiteSpace(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        var content = (FrameworkElement)window.Content;
        ServiceBusEmulatorExplorer.ReadmeScreenshot.WpfScreenshot.SaveWindowContent(window,
            System.IO.Path.Combine(directory, $"delete43-{width}x{height}-{state}.png"),
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
    }
    private static void Focus(Fixture fixture, MessageRow row)
    {
        DataGrid grid = fixture.Grid;
        grid.UpdateLayout();
        WaitForRow(grid, row);
        grid.SelectedItem = row; // Raises the real SelectionChanged route used by inspector focus.
        grid.Focus();
        Drain();

        Assert.Same(row, fixture.Workspace.Browse.FocusedMessage);
        Assert.Same(row.Delivery, fixture.Workspace.Inspector.Current);
        Assert.False(row.IsSelected);
        Assert.True(grid.IsKeyboardFocusWithin);
    }

    private static void ToggleCheckbox(Fixture fixture, MessageRow row)
    {
        DataGridRow rowContainer = WaitForRow(fixture.Grid, row);
        var checkbox = FindVisualChildren<CheckBox>(rowContainer).Single();
        var peer = UIElementAutomationPeer.CreatePeerForElement(checkbox)
            ?? throw new InvalidOperationException("The message selection checkbox has no UI Automation peer.");
        var toggle = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider
            ?? throw new InvalidOperationException("The message selection checkbox does not expose Toggle.");
        toggle.Toggle();
        Drain();
        Assert.Equal(row.IsSelected, toggle.ToggleState == ToggleState.On);
    }

    private static DataGridRow WaitForRow(DataGrid grid, MessageRow row)
    {
        DataGridRow? container = null;
        Wait(() =>
        {
            grid.UpdateLayout();
            container = grid.ItemContainerGenerator.ContainerFromItem(row) as DataGridRow;
            return container is not null;
        });
        return container!;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static MessageRow CreateRow(MessageBucket bucket, long sequence)
    {
        var message = new ExplorerMessage($"message-{sequence}", sequence, "body", "body", 4, null, null, 0,
            "text/plain", null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new MessageRow(new(new(1, new(EntityKind.Queue, "orders"), bucket, sequence), message), TimestampDisplay.Utc);
    }

    private static void OnSta(Action action)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Delete selection WPF proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Delete selection UI did not reach the expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class Fixture : IDisposable
    {
        private static readonly ServiceBusReceivedMessage DlqMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{\"order\":80341}"), messageId: "dlq-original", sequenceNumber: 1,
            enqueuedTime: new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), contentType: "application/json");

        public InvestigationWorkspace Workspace { get; }
        public InvestigationWindow Window { get; }
        public DataGrid Grid => (DataGrid)Window.FindName("MessageGrid");
        public Button Delete => (Button)Window.FindName("DeleteButton");

        public Fixture(int width, int height)
        {
            Workspace = new InvestigationWorkspace(new Store(),
                new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => new Messages()),
                null, (_, _) => new Receiver());
            Window = new InvestigationWindow(Workspace);
            Window.Show();
            Complete(Workspace.ConnectAsync());
            Complete(Workspace.Browse.SelectAsync(Workspace.Browse.AllEntities().Single(), true));
            Assert.Single(Workspace.Browse.Messages);
            Window.Width = width;
            Window.Height = height;
            Window.UpdateLayout();
            Drain();
        }

        public void Dispose()
        {
            foreach (Window dialog in Window.OwnedWindows.Cast<Window>().ToArray()) dialog.Close();
            Complete(Workspace.DisposeAsync().AsTask());
            Window.Close();
            Wait(() => !Window.IsVisible);
        }

        private static void Complete(Task task)
        {
            Wait(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        private sealed class Store : IWorkspacePreferencesStore
        {
            private WorkspacePreferences value = new()
            {
                Profiles = [new("delete-selection", new ConnectionProfile("Delete selection",
                    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=test;UseDevelopmentEmulator=true;", "admin"))],
                SelectedProfileId = "delete-selection", CloseToTray = false, WasConnected = false
            };

            public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(value));
            public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
            { value = preferences; return Task.CompletedTask; }
        }

        private sealed class Factory : IServiceBusClientFactory
        {
            public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException();
            public ServiceBusClient RuntimeClient => throw new InvalidOperationException();
            public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class Browser : IInvestigationEntityBrowser
        {
            public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(
                new EntityDiscoverySnapshot([new(new ServiceBusEntityNode(EntityKind.Queue, "orders", null, new(1, 0, 0, 1),
                    new("orders", "Active", null, null, null, null, null, null, null)),
                    new(new(0, CountAvailability.Known), new(1, CountAvailability.Known), new(0, CountAvailability.Known)))],
                    DateTimeOffset.UtcNow, true, []));
        }

        private sealed class Messages : IServiceBusMessageService
        {
            public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
                long? fromSequenceNumber, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExplorerMessage>>(
                bucket == MessageBucket.DeadLetter && (fromSequenceNumber is null || DlqMessage.SequenceNumber >= fromSequenceNumber)
                    ? [MessageProjection.Create(DlqMessage)] : []);
            public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
        }

        private sealed class Receiver : IDlqDeleteReceiver
        {
            public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("This selection proof must not acquire or delete broker deliveries.");
            public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("This selection proof must not delete broker deliveries.");
            public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
