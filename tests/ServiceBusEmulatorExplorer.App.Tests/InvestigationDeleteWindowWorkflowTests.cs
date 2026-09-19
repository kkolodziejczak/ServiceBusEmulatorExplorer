using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ICSharpCode.AvalonEdit;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationDeleteWindowWorkflowTests
{
    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void ConfirmedRowsDisappearWhileUncertainRowsAndUnrelatedDraftRemain(int width, int height) => OnSta(() =>
    {
        var fixture = new Fixture(width, height);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool pending = false;
        fixture.Receiver.Complete = async (message, token) =>
        {
            if (message.SequenceNumber == 1) return;
            pending = true;
            await release.Task.WaitAsync(token);
            throw new IOException("Completion response lost");
        };
        try
        {
            var rows = fixture.Workspace.Browse.Messages.ToArray();
            fixture.Workspace.Browse.FocusedMessage = rows[2];
            fixture.Workspace.Inspector.Document.Text = "{\"unrelatedDraft\":true}";
            rows[0].IsSelected = true;
            rows[1].IsSelected = true;
            fixture.ConfirmDelete();
            Wait(() => pending);
            Assert.Equal("Cancel delete", AutomationProperties.GetName(fixture.Delete));
            Assert.True(fixture.Delete.IsEnabled);
            Assert.False(((Button)fixture.Window.FindName("ReplayButton")).IsEnabled);
            Assert.Equal(3, fixture.Workspace.Browse.Messages.Count);
            Capture(fixture.Window, $"delete-workflow-pending-{width}x{height}");

            release.TrySetResult();
            Wait(() => fixture.Workspace.Activity.Any(entry => entry.Message == "Delete: 1 confirmed, 1 not confirmed."));

            Assert.Equal([2L, 3L], fixture.Workspace.Browse.Messages.Select(row => row.Key.SequenceNumber));
            Assert.Same(rows[2], fixture.Workspace.Browse.FocusedMessage);
            Assert.True(fixture.Workspace.Inspector.HasDraft(rows[2].Key));
            Assert.Equal("{\"unrelatedDraft\":true}", fixture.Workspace.Inspector.Document.Text);
            Assert.Contains(fixture.Workspace.Activity, entry => entry.Message.Contains("sequence 1: deleted."));
            Assert.Contains(fixture.Workspace.Activity, entry => entry.Message.Contains("sequence 2: outcome uncertain; retained in view until verified."));
            Assert.Equal([1L, 2L], fixture.Receiver.CompletionAttempts);
            Assert.Equal([2L, 3L], fixture.Receiver.Abandoned);
            Assert.Equal("Delete", AutomationProperties.GetName(fixture.Delete));
            Capture(fixture.Window, $"delete-workflow-result-{width}x{height}");
        }
        finally { release.TrySetResult(); fixture.Dispose(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void PendingDeleteCanBeCanceledWithoutRemovingUnconfirmedRows() => OnSta(() =>
    {
        var fixture = new Fixture(1100, 800);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Receiver.Receive = async token =>
        {
            received.TrySetResult();
            using var registration = token.Register(() => canceled.TrySetResult());
            await release.Task;
            token.ThrowIfCancellationRequested();
            return [];
        };
        try
        {
            fixture.Workspace.Browse.Messages[0].IsSelected = true;
            fixture.ConfirmDelete();
            Wait(() => received.Task.IsCompleted);
            Assert.Equal("Cancel delete", AutomationProperties.GetName(fixture.Delete));
            Invoke(fixture.Delete);
            Wait(() => canceled.Task.IsCompleted);
            Assert.False(fixture.Delete.IsEnabled);
            Capture(fixture.Window, "delete-workflow-cancel-pending");
            release.TrySetResult();
            Wait(() => fixture.Workspace.Activity.Any(entry => entry.Message == "Delete: 0 confirmed, 1 not confirmed."));
            Assert.Equal(3, fixture.Workspace.Browse.Messages.Count);
            Assert.Empty(fixture.Receiver.CompletionAttempts);
            Assert.Contains(fixture.Workspace.Activity, entry => entry.Message.Contains("not attempted; retained in view"));
            Assert.True(fixture.Delete.IsEnabled);
        }
        finally { release.TrySetResult(); fixture.Dispose(); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void ConfirmationFromPriorConnectionCannotDeleteAfterReconnect() => OnSta(() =>
    {
        var fixture = new Fixture(1100, 800);
        try
        {
            fixture.Workspace.Browse.Messages[0].IsSelected = true;
            fixture.ConfirmDelete(() =>
            {
                Complete(fixture.Workspace.DisconnectAsync());
                Complete(fixture.Workspace.ConnectAsync());
                Complete(fixture.Workspace.Browse.SelectAsync(fixture.Workspace.Browse.AllEntities().Single(), true));
            });
            Assert.Equal(0, fixture.Receiver.ReceiveCalls);
            Assert.Equal(3, fixture.Workspace.Browse.Messages.Count);
            Assert.DoesNotContain(fixture.Workspace.Activity, entry => entry.Message.StartsWith("Delete:"));
        }
        finally { fixture.Dispose(); }
    });

    private sealed class Fixture : IDisposable
    {
        public InvestigationWorkspace Workspace { get; }
        public InvestigationWindow Window { get; }
        public Receiver Receiver { get; } = new();
        public Button Delete => (Button)Window.FindName("DeleteButton");

        public Fixture(int width, int height)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Workspace = new InvestigationWorkspace(new Store(),
                new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => new Messages()),
                null, (_, _) => Receiver);
            Window = new InvestigationWindow(Workspace);
            Window.Show();
            Complete(Workspace.ConnectAsync());
            Complete(Workspace.Browse.SelectAsync(Workspace.Browse.AllEntities().Single(), true));
            Assert.Equal(3, Workspace.Browse.Messages.Count);
            Window.Width = width;
            Window.Height = height;
            Window.UpdateLayout();
        }

        public void ConfirmDelete(Action? beforeConfirmation = null)
        {
            bool handled = false;
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Window.OwnedWindows.OfType<DeleteMessagesWindow>().FirstOrDefault();
                if (dialog is null) return;
                timer.Stop();
                try
                {
                    beforeConfirmation?.Invoke();
                    ((TextBox)dialog.FindName("DeleteConfirmationInput")).Text = "DELETE";
                    Invoke((Button)dialog.FindName("ConfirmDeleteButton"));
                    handled = true;
                }
                catch (Exception exception) { failure = exception; dialog.Close(); handled = true; }
            };
            timer.Start();
            try
            {
                Assert.True(Delete.IsEnabled);
                Invoke(Delete);
                Wait(() => handled);
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally { timer.Stop(); }
        }

        public void Dispose()
        {
            foreach (var row in Workspace.Browse.Messages.ToArray())
            {
                Workspace.Inspector.Select(row.Delivery);
                Workspace.Inspector.DiscardCurrent();
            }
            foreach (Window dialog in Window.OwnedWindows.Cast<Window>().ToArray()) dialog.Close();
            Complete(Workspace.DisposeAsync().AsTask());
            Window.Close();
            Wait(() => !Window.IsVisible);
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Delete workflow proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Delete UI did not reach the expected state.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
        .GetPattern(PatternInterface.Invoke)).Invoke();

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        foreach (string control in new[] { "DeleteButton", "ReplayButton", "DiscardButton" })
        {
            var button = (Button)window.FindName(control);
            if (!button.IsVisible) continue;
            var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0
                && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, $"{control} clipped: {bounds}");
        }
        var editor = (TextEditor)window.FindName("BodyEditor");
        var textView = editor.TextArea.TextView;
        Assert.True(textView.ActualHeight >= textView.DefaultLineHeight,
            $"The document viewport must display at least one full line: height {textView.ActualHeight}, line {textView.DefaultLineHeight}.");
        textView.EnsureVisualLines();
        Assert.NotEmpty(textView.VisualLines);
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    private static readonly ServiceBusReceivedMessage[] BrokerMessages = Enumerable.Range(1, 3).Select(sequence =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{\"order\":80341}"),
            messageId: "checkout-original-message-" + sequence, sequenceNumber: sequence,
            enqueuedTime: new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), contentType: "application/json")).ToArray();

    private sealed class Receiver : IDlqDeleteReceiver
    {
        public int ReceiveCalls { get; private set; }
        public List<long> CompletionAttempts { get; } = [];
        public List<long> Abandoned { get; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<ServiceBusReceivedMessage>>>? Receive { get; set; }
        public Func<ServiceBusReceivedMessage, CancellationToken, Task> Complete { get; set; } = (_, _) => Task.CompletedTask;
        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken)
        {
            ReceiveCalls++;
            return Receive?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(ReceiveCalls == 1 ? BrokerMessages : []);
        }
        public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
        { CompletionAttempts.Add(message.SequenceNumber); return Complete(message, cancellationToken); }
        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
        { Abandoned.Add(message.SequenceNumber); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        private WorkspacePreferences value = new()
        {
            Profiles = [new("render", new ConnectionProfile("Delete render",
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=test;UseDevelopmentEmulator=true;", "admin"))],
            SelectedProfileId = "render", CloseToTray = false, WasConnected = false
        };
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(value));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) { value = preferences; return Task.CompletedTask; }
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
            new EntityDiscoverySnapshot([new(new ServiceBusEntityNode(EntityKind.Queue, "orders", null, new(0, 3, 0, 3),
                new("orders", "Active", null, null, null, null, null, null, null)),
                new(new(0, CountAvailability.Known), new(3, CountAvailability.Known), new(0, CountAvailability.Known)))],
                DateTimeOffset.UtcNow, true, []));
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExplorerMessage>>(
            BrokerMessages.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                .Take(take).Select(MessageProjection.Create).ToArray());
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
