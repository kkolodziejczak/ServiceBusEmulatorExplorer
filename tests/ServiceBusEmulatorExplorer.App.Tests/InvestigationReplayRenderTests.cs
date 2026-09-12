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
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationReplayRenderTests
{
    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Replay_actions_validate_edits_disable_during_send_and_preserve_originals(int width, int height) =>
        OnSta(() => Exercise(width, height));

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Subscription_replay_confirms_parent_topic_fanout_every_time() => OnSta(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var sender = new Sender();
        var workspace = new InvestigationWorkspace(new Store(),
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(true), _ => new Messages()), _ => sender);
        var window = new InvestigationWindow(workspace);
        try
        {
            window.Show();
            Complete(dispatcher, workspace.ConnectAsync());
            Complete(dispatcher, workspace.Browse.SelectAsync(workspace.Browse.AllEntities().Single(node => node.Kind == "Subscription"), true));
            var replay = (Button)window.FindName("ReplayButton");
            Respond(false);
            Assert.Empty(sender.Sends);
            Respond(true);
            Assert.Single(sender.Sends);
            Respond(false);
            Assert.Single(sender.Sends);
            workspace.Browse.SetAllChecked(true);
            Respond(true, false);
            Assert.Equal(2, sender.Sends.Count);
            Assert.Contains(workspace.Activity, entry => entry.Message.Contains("Replay batch: 1 sent") && entry.Message.Contains("1 canceled"));

            void Respond(params bool[] responses)
            {
                bool handled = false;
                int responseIndex = 0;
                Exception? failure = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) =>
                {
                    var dialog = window.OwnedWindows.OfType<ProfileWarningWindow>().FirstOrDefault();
                    if (dialog is null) return;
                    try
                    {
                        string message = ((TextBlock)dialog.FindName("WarningMessageText")).Text;
                        Assert.Contains("Other matching subscriptions can receive it", message);
                        Assert.Contains("original DLQ message will remain", message);
                        Assert.Equal("Replay to parent topic", ((TextBlock)dialog.FindName("WarningHeading")).Text);
                        var cancel = (Button)dialog.FindName("CancelButton");
                        Assert.True(cancel.IsDefault);
                        Assert.True(cancel.IsKeyboardFocused, "The safe Cancel action should receive initial modal focus.");
                        foreach (string name in new[] { "CancelButton", "ContinueButton" })
                        {
                            var button = (FrameworkElement)dialog.FindName(name);
                            var bounds = button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                            Assert.True(bounds.Width > 0 && bounds.Right <= dialog.ActualWidth && bounds.Bottom <= dialog.ActualHeight);
                        }
                        Capture(dialog, "replay-topic-confirmation");
                        Invoke((Button)dialog.FindName(responses[responseIndex++] ? "ContinueButton" : "CancelButton"));
                        handled = responseIndex == responses.Length;
                        if (handled) timer.Stop();
                    }
                    catch (Exception ex) { failure = ex; dialog.Close(); handled = true; }
                };
                timer.Start();
                try
                {
                    Invoke(replay);
                    Wait(dispatcher, () => handled && replay.IsEnabled);
                    if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                }
                finally { timer.Stop(); }
            }
        }
        finally
        {
            foreach (Window owned in window.OwnedWindows.Cast<Window>().ToArray()) owned.Close();
            window.Close();
            Wait(dispatcher, () => !window.IsVisible);
            Complete(dispatcher, workspace.DisposeAsync().AsTask());
        }
    });

    private static void Exercise(int width, int height)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var sender = new Sender();
        var workspace = new InvestigationWorkspace(new Store(),
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => new Messages()), _ => sender);
        var window = new InvestigationWindow(workspace);
        try
        {
            window.Show();
            Wait(dispatcher, () => window.IsVisible);
            Complete(dispatcher, workspace.ConnectAsync());
            Complete(dispatcher, workspace.Browse.SelectAsync(workspace.Browse.AllEntities().Single(), true));
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
            var replay = (Button)window.FindName("ReplayButton");
            var error = (TextBlock)window.FindName("EditError");
            var original = workspace.Browse.Messages[0];
            workspace.Browse.FocusedMessage = original;
            Assert.True(replay.IsEnabled);
            Assert.Equal("Replay", AutomationProperties.GetName(replay));
            Capture(window, $"replay-clean-{width}x{height}");
            Invoke(replay);
            Wait(dispatcher, () => sender.Sends.Count == 1 && replay.IsEnabled);
            Assert.Same(original, workspace.Browse.Messages[0]);
            Assert.Equal(original.Delivery.Message.RawBody!.ToArray(), sender.Sends[0].Body.ToArray());

            var activeDelivery = new MessageDelivery(original.Key with { Bucket = MessageBucket.Active }, original.Delivery.Message);
            var activeRow = new MessageRow(activeDelivery, workspace.Preferences.TimestampDisplay) { IsSelected = true };
            workspace.Browse.Messages.Add(activeRow);
            original.IsSelected = true;
            Assert.False(replay.IsEnabled);
            Assert.Contains("Select only dead-letter", error.Text);
            workspace.Browse.Messages.Remove(activeRow);
            original.IsSelected = false;

            workspace.Inspector.Document.Text = "{";
            Assert.False(replay.IsEnabled);
            Assert.Equal(Visibility.Visible, error.Visibility);
            Assert.Contains("Complete the JSON", error.Text);
            Capture(window, $"replay-invalid-{width}x{height}");
            workspace.Inspector.Document.Text = "{\"edited\":true}";
            Assert.True(replay.IsEnabled);
            Assert.Equal("Edit and Replay", AutomationProperties.GetName(replay));
            workspace.Browse.SetAllChecked(true);
            Assert.False(replay.IsEnabled);
            Assert.Contains("Select only this message", error.Text);
            workspace.Browse.SetAllChecked(false);
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.Send = () => pending.Task;
            Invoke(replay);
            Wait(dispatcher, () => sender.Sends.Count == 2);
            Assert.False(replay.IsEnabled);
            Assert.Equal("Replaying…", AutomationProperties.GetName(replay));
            Assert.False(((Button)window.FindName("DiscardButton")).IsEnabled);
            Capture(window, $"replay-pending-{width}x{height}");
            workspace.Browse.FocusedMessage = workspace.Browse.Messages[1];
            Assert.True(workspace.Inspector.HasDraft(original.Key));
            pending.SetResult();
            Wait(dispatcher, () => replay.IsEnabled);
            Assert.False(workspace.Inspector.HasDraft(original.Key));
            workspace.Browse.FocusedMessage = original;
            Assert.False(workspace.Inspector.IsDirty);
            Assert.Equal("{\"edited\":true}", sender.Sends[1].Body.ToString());
            Assert.Same(original, workspace.Browse.Messages[0]);

            workspace.Inspector.Document.Text = "{\"uncertain\":true}";
            sender.Send = () => throw new IOException("Lost acknowledgement");
            Invoke(replay);
            Wait(dispatcher, () => sender.Sends.Count == 3 && replay.IsEnabled);
            Assert.True(workspace.Inspector.IsDirty);
            Assert.Contains(workspace.Activity, entry => entry.Warning && entry.Message.Contains("send outcome uncertain"));
            Capture(window, $"replay-uncertain-{width}x{height}");
            workspace.Inspector.DiscardCurrent();
            sender.Send = () => Task.CompletedTask;
            workspace.Browse.SetAllChecked(true);
            Assert.Equal("Replay (2)", AutomationProperties.GetName(replay));
            Invoke(replay);
            Wait(dispatcher, () => sender.Sends.Count == 5 && replay.IsEnabled);
            Assert.Equal(2, workspace.Browse.Messages.Count);
            workspace.Browse.SetAllChecked(false);
            Complete(dispatcher, workspace.Browse.SelectAsync(workspace.Browse.AllEntities().Single(), false));
            Assert.Equal(Visibility.Collapsed, replay.Visibility);
            Complete(dispatcher, workspace.Browse.SelectAsync(workspace.Browse.AllEntities().Single(), true));
            var lateSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sender.Send = () => lateSend.Task;
            Invoke(replay);
            Wait(dispatcher, () => sender.Sends.Count == 6);
            string oldReplayId = sender.Sends[^1].MessageId;
            Complete(dispatcher, workspace.DisconnectAsync());
            Complete(dispatcher, workspace.ConnectAsync());
            Complete(dispatcher, workspace.Browse.SelectAsync(workspace.Browse.AllEntities().Single(), true));
            lateSend.SetResult();
            Wait(dispatcher, () => replay.IsEnabled);
            Assert.DoesNotContain(workspace.Activity, entry => entry.Message.Contains(oldReplayId));
        }
        finally
        {
            workspace.Inspector.DiscardCurrent();
            window.Close();
            Wait(dispatcher, () => !window.IsVisible);
            Complete(dispatcher, workspace.DisposeAsync().AsTask());
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Replay render proof exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Dispatcher dispatcher, Task task)
    {
        Wait(dispatcher, () => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void Wait(Dispatcher dispatcher, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Replay UI did not reach the expected state.");
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!
        .GetPattern(PatternInterface.Invoke)).Invoke();

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        if (window.FindName("ReplayButton") is FrameworkElement replay)
        {
            var bounds = replay.TransformToAncestor(window).TransformBounds(new Rect(replay.RenderSize));
            Assert.True(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0
                && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, $"Replay action clipped: {bounds}");
        }
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        string directory = Path.Combine(root!.FullName, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    private sealed class Store : IWorkspacePreferencesStore
    {
        private WorkspacePreferences value = new()
        {
            Profiles = [new("render", new ConnectionProfile("Replay render", "runtime", "admin"))],
            SelectedProfileId = "render", CloseToTray = false, WasConnected = false
        };
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new PreferencesLoadResult(value));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) { value = preferences; return Task.CompletedTask; }
    }

    private sealed class Sender : IReplayCopySender
    {
        public List<ServiceBusMessage> Sends { get; } = [];
        public Func<Task> Send { get; set; } = () => Task.CompletedTask;
        public Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken)
        { Sends.Add(message); return Send(); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Factory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException();
        public ServiceBusClient RuntimeClient => throw new InvalidOperationException();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Browser(bool subscription = false) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            var observations = new List<EntityObservation>();
            if (subscription) observations.Add(Observation(EntityKind.Topic, "order-events", null));
            observations.Add(Observation(subscription ? EntityKind.Subscription : EntityKind.Queue,
                "orders-with-a-long-operational-queue-name-for-investigation", subscription ? "order-events" : null));
            return Task.FromResult(new EntityDiscoverySnapshot(observations, DateTimeOffset.UtcNow, true, []));
        }

        private static EntityObservation Observation(EntityKind kind, string name, string? topic) => new(
            new ServiceBusEntityNode(kind, name, topic, new EntityRuntimeCounts(2, 2, 0, 4),
                new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
            new(new(2, CountAvailability.Known), new(2, CountAvailability.Known), new(0, CountAvailability.Known)));
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExplorerMessage>>(
            Enumerable.Range(1, 2).Where(sequence => fromSequenceNumber is null || sequence >= fromSequenceNumber).Take(take)
                .Select(sequence => new ExplorerMessage("checkout-original-with-long-message-id-for-order-correlation-" + sequence,
                    sequence, "{\"order\":80341}", "{\"order\":80341}", 15, DateTimeOffset.UtcNow, null, 1, "application/json", "checkout-80341",
                    null, "Order submitted", new Dictionary<string, object?>(), new Dictionary<string, object?>())
                    { RawBody = BinaryData.FromString("{\"order\":80341}") }).ToArray());
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}

