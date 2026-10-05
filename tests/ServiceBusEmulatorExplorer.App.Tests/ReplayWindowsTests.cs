using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed partial class ReplayWindowsTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Replay_action_reviews_exact_ID_cancel_is_nonmutating_and_confirm_sends_reviewed_copy()
    {
        OnSta(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var sender = new CapturingReplaySender();
            InvestigationWorkspace workspace = CreateWorkspace(sender);
            var owner = new InvestigationWindow(workspace)
            {
                Width = 1500,
                Height = 1000,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            try
            {
                owner.Show();
                PumpUntil(dispatcher, () => owner.IsVisible, "Investigation to show");
                Complete(dispatcher, workspace.ConnectAsync());
                EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
                Complete(dispatcher, workspace.Browse.SelectAsync(queue, deadLetter: true));
                PumpUntil(dispatcher, () => workspace.Surface.FocusedMessage?.IsDeadLetter == true,
                    "the queue DLQ delivery to become the focused message");
                PreparedReplay candidate = workspace.PrepareReplay(workspace.Surface.FocusedMessage!.Delivery);
                Assert.Equal("original-order-10482", candidate.Delivery.Message.MessageId);
                var standalone = new ReplayReviewWindow(workspace.SelectedProfile, [candidate]) { Owner = owner, Width = 640 };
                bool standaloneLoaded = false;
                standalone.Loaded += (_, _) => dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    standaloneLoaded = true;
                    standalone.UpdateLayout();
                    Assert.Single(standalone.Rows);
                    Assert.Equal(candidate.Reservation.MessageId,
                        Descendants(standalone).OfType<TextBox>().Single(box => box.Name == "MessageIdBox").Text);
                    Capture(standalone, "Replay-29-review-direct-640.png");
                    Click((Button)standalone.FindName("CancelButton")!);
                }));
                Assert.False(standalone.ShowDialog());
                Assert.True(standaloneLoaded, "The standalone review window must load and render before routing the main action.");
                var replayButton = (Button)owner.FindName("ReplayButton")!;
                Assert.Equal(Visibility.Visible, replayButton.Visibility);
                Assert.True(replayButton.IsEnabled, "The selected current DLQ delivery must enable Replay.");

                ReplayReviewWindow? canceledReview = RouteToReview(dispatcher, owner, workspace, sender, review =>
                {
                    Assert.Equal("ReplayReviewWindow", review.GetType().Name);
                    Assert.Single(review.Rows);
                    Assert.Equal("original-order-10482", review.Rows[0].OriginalId);
                    Assert.Equal("Replay window proof", ((TextBlock)review.FindName("ProfileText")!).Text);
                    Assert.Equal("Original body", review.Rows[0].BodySummary);
                    Assert.Null(review.FindName("ApplicationProperties"));
                    Capture(review, "Replay-29-review-cancel-640.png");
                    Click((Button)review.FindName("CancelButton")!);
                });
                Assert.NotNull(canceledReview);
                Assert.Empty(sender.Messages);
                Assert.Empty(workspace.Preferences.ReplayAttempts);
                Assert.Empty(workspace.Preferences.ReplayFamilies);

                var editor = (JsonEditor)owner.FindName("BodyEditor")!;
                editor.Document.Insert(0, " ");
                string reviewedBody = editor.Text;
                ReplayReviewWindow? confirmedReview = RouteToReview(dispatcher, owner, workspace, sender, review =>
                {
                    ReplayReviewRow row = Assert.Single(review.Rows);
                    Assert.Equal("Modified JSON", row.BodySummary);
                    var idBox = Descendants(review).OfType<TextBox>().Single(box => box.Name == "MessageIdBox");
                    idBox.Text = "";
                    Assert.NotNull(row.Problem);
                    Assert.False(((Button)review.FindName("ReplayConfirmButton")!).IsEnabled);
                    var reset = Descendants(review).OfType<Button>().Single(button => button.Name == "ResetButton");
                    Click(reset);
                    Assert.Equal(row.Prepared.Reservation.MessageId, idBox.Text);
                    idBox.Text = "order-10482-replay-reviewed";
                    Assert.Null(row.Problem);
                    Assert.True(((Button)review.FindName("ReplayConfirmButton")!).IsEnabled);
                    Capture(review, "Replay-29-review-edited-640.png");
                    Click((Button)review.FindName("ReplayConfirmButton")!);
                });
                Assert.NotNull(confirmedReview);
                PumpUntil(dispatcher, () => sender.Messages.Count == 1, "the reviewed replay to reach the sender");
                var sent = Assert.Single(sender.Messages);
                Assert.Equal("order-10482-replay-reviewed", sent.MessageId);
                Assert.Equal(BinaryData.FromString(reviewedBody).ToArray(), sent.Body.ToArray());
                var saved = Assert.Single(workspace.Preferences.ReplayAttempts);
                Assert.Equal("order-10482-replay-reviewed", saved.Reservation.MessageId);
                Assert.Equal(1, saved.Reservation.Family.LastAttempt);
            }
            finally
            {
                workspace.ConfirmDiscard = () => Task.FromResult(true);
                if (owner.IsVisible)
                {
                    owner.Close();
                    PumpUntil(dispatcher, () => !owner.IsVisible, "Investigation to close");
                }
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static ReplayReviewWindow? RouteToReview(Dispatcher dispatcher, InvestigationWindow owner,
        InvestigationWorkspace workspace, CapturingReplaySender sender, Action<ReplayReviewWindow> interact)
    {
        ReplayReviewWindow? review = null;
        bool done = false;
        Exception? callbackFailure = null;
        Action inspect = null!;
        inspect = () =>
        {
            try
            {
                review = owner.OwnedWindows.OfType<ReplayReviewWindow>().SingleOrDefault(candidate => candidate.IsVisible);
                if (review is not null)
                {
                    review.Width = 640;
                    review.UpdateLayout();
                    interact(review);
                    done = true;
                }
                else if (workspace.Preferences.ReplayAttempts.Count > sender.Messages.Count || sender.Messages.Count > 0)
                {
                    // This branch makes the initial red check fail when Replay bypasses its review window.
                    done = true;
                }
                else dispatcher.BeginInvoke(DispatcherPriority.Background, inspect);
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                if (review?.IsVisible == true) review.Close();
                done = true;
            }
        };
        // Queue before the routed action. ShowDialog pumps this dispatcher work after the review is loaded.
        dispatcher.BeginInvoke(DispatcherPriority.Normal, inspect);
        var button = (Button)owner.FindName("ReplayButton")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        PumpUntil(dispatcher, () => done, "replay review or the prior direct-send route");
        if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
        return review;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void SettleLayout(Dispatcher dispatcher, FrameworkElement element)
    {
        var operation = dispatcher.InvokeAsync(element.UpdateLayout, DispatcherPriority.ApplicationIdle);
        PumpUntil(dispatcher, () => operation.Task.IsCompleted, "WPF layout to settle at ApplicationIdle");
        operation.Task.GetAwaiter().GetResult();
    }

    private static InvestigationWorkspace CreateWorkspace(CapturingReplaySender sender, FakeMessages? messages = null,
        IDlqDeleteReceiver? deleteReceiver = null, WorkspacePreferences? initialPreferences = null)
    {
        var preferences = initialPreferences ?? new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile("replay-windows", ConnectionProfileDefaults.LocalEmulator with { Name = "Replay window proof" })],
            SelectedProfileId = "replay-windows",
            LogExpanded = false
        };
        var queue = new ServiceBusEntityNode(EntityKind.Queue, "orders", null,
            new EntityRuntimeCounts(0, 1, 0, 1), new EntityMetadata("orders", "Active", null, null, null, null, null, null, null));
        var snapshot = new EntityDiscoverySnapshot(
            [new EntityObservation(queue, new EntityCountObservation(
                new(null, CountAvailability.Known), new(null, CountAvailability.Known), new(null, CountAvailability.Known)))],
            DateTimeOffset.UtcNow, true, []);
        messages ??= new FakeMessages();
        var workflow = new BrokerConnectionWorkflow(() => new FakeFactory(), _ => new FakeBrowser(snapshot), _ => messages);
        return new InvestigationWorkspace(new FakeStore(preferences), workflow, _ => sender,
            deleteReceiver is null ? null : (_, _) => deleteReceiver);
    }

    private static void Capture(Window window, string filename)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        var content = (FrameworkElement)window.Content;
        SettleLayout(window.Dispatcher, content);
        WpfScreenshot.SaveWindowContent(window, ScreenshotPath(filename),
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), minimumBytes: 1000);
    }

    private static string ScreenshotPath(string filename)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))
            && !File.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("The repository root was not found for screenshot output.");
        string output = Path.Combine(directory.FullName, "artifacts", "replay-window-proof");
        Directory.CreateDirectory(output);
        return Path.Combine(output, filename);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Replay review WPF proof exceeded its 60-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Dispatcher dispatcher, Task task)
    {
        PumpUntil(dispatcher, () => task.IsCompleted, "workspace operation to complete");
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, string expectation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Timed out waiting for {expectation}.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

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

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        public static readonly EntityAddress Queue = new(EntityKind.Queue, "orders");
        public static readonly ExplorerMessage Message = new("original-order-10482", 11,
            "{\"eventType\":\"OrderPlaced\",\"data\":{\"orderId\":\"ORD-10482\"}}",
            "OrderPlaced · ORD-10482", 60, DateTimeOffset.UtcNow, null, 0,
            "application/json", "ORD-10482", null, "OrderPlaced", new Dictionary<string, object?>(), new Dictionary<string, object?>())
        { RawBody = BinaryData.FromString("{\"eventType\":\"OrderPlaced\",\"data\":{\"orderId\":\"ORD-10482\"}}") };

        private readonly List<(EntityAddress Address, MessageBucket Bucket, ExplorerMessage Message)> deliveries =
            [(Queue, MessageBucket.DeadLetter, Message)];
        private long nextSequence = 30;
        public (EntityAddress Address, MessageBucket Bucket)? Failure { get; set; }
        public bool ContainsOriginal => deliveries.Any(item => item.Bucket == MessageBucket.DeadLetter && item.Message.SequenceNumber == Message.SequenceNumber);

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            if (Failure == (address, bucket)) throw new IOException($"Test broker failure for {bucket}.");
            IReadOnlyList<ExplorerMessage> result = deliveries.Where(item => item.Address == address && item.Bucket == bucket
                    && (fromSequenceNumber is null || item.Message.SequenceNumber >= fromSequenceNumber))
                .OrderBy(item => item.Message.SequenceNumber).Take(take).Select(item => item.Message).ToArray();
            return Task.FromResult(result);
        }

        public void AddReplay(string id, BinaryData body) => deliveries.Add((Queue, MessageBucket.Active,
            new ExplorerMessage(id, nextSequence++, body.ToString(), id, body.ToMemory().Length, DateTimeOffset.UtcNow,
                null, 0, "application/json", null, null, "OrderPlaced", new Dictionary<string, object?>(),
                new Dictionary<string, object?>()) { RawBody = body, BrokerState = ServiceBusMessageState.Active }));

        public void Move(string id, MessageBucket from, MessageBucket to)
        {
            int index = deliveries.FindIndex(item => item.Bucket == from && item.Message.MessageId == id);
            Assert.True(index >= 0, $"Replay '{id}' was not found in {from}.");
            var match = deliveries[index];
            deliveries[index] = (match.Address, to, match.Message with
            { BrokerState = to == MessageBucket.DeadLetter ? null : ServiceBusMessageState.Active });
        }

        public void ClearReplayCopies(IEnumerable<string> ids) =>
            deliveries.RemoveAll(item => ids.Contains(item.Message.MessageId, StringComparer.Ordinal));

        public ServiceBusReceivedMessage? ReceiveOriginal(long sequence) => deliveries
            .Where(item => item.Bucket == MessageBucket.DeadLetter && item.Message.SequenceNumber == sequence)
            .Select(item => ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: item.Message.RawBody ?? BinaryData.FromString(item.Message.Body), messageId: item.Message.MessageId,
                sequenceNumber: item.Message.SequenceNumber, enqueuedTime: item.Message.EnqueuedTime ?? DateTimeOffset.UtcNow,
                correlationId: item.Message.CorrelationId, subject: item.Message.Subject, contentType: item.Message.ContentType))
            .FirstOrDefault();

        public void Remove(long sequence) => deliveries.RemoveAll(item => item.Bucket == MessageBucket.DeadLetter && item.Message.SequenceNumber == sequence);

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapturingReplaySender : IReplayCopySender
    {
        private readonly FakeMessages? messages;
        public CapturingReplaySender(FakeMessages? messages = null) => this.messages = messages;
        public List<ServiceBusMessage> Messages { get; } = [];
        public Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            messages?.AddReplay(message.MessageId, message.Body);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingDeleteReceiver(FakeMessages messages) : IDlqDeleteReceiver
    {
        public List<long> Completed { get; } = [];
        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken)
        {
            var original = messages.ReceiveOriginal(FakeMessages.Message.SequenceNumber);
            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(original is null ? [] : [original]);
        }
        public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
        {
            Completed.Add(message.SequenceNumber);
            messages.Remove(message.SequenceNumber);
            return Task.CompletedTask;
        }
        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
