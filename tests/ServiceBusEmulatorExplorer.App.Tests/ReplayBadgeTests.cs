using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class ReplayBadgeTests
{
    private const string ProfileId = "replay-badge-profile";
    private const string TopicName = "long-topic-name-for-replay-badge-proof";
    private const string SubscriptionName = "subscription-with-a-long-source-name-that-must-not-hide-the-active-or-dlq-state-badge";
    private static readonly string ReplayId = "replay-" + "r".PadRight(86, 'r');

    [Fact]
    public async Task Browse_and_search_mark_only_exact_current_profile_namespace_attempts()
    {
        var fixture = CreateFixture();
        await using InvestigationWorkspace workspace = fixture.Workspace;
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();

        EntityNode subscription = Assert.Single(workspace.Browse.AllEntities(), node => node.Kind == nameof(EntityKind.Subscription));
        await workspace.Browse.SelectAsync(subscription, deadLetter: false);
        MessageRow browseReplay = Assert.Single(workspace.Browse.Messages, row => row.MessageId == ReplayId);
        MessageRow substringMatch = Assert.Single(workspace.Browse.Messages, row => row.MessageId == ReplayId + "-ordinary-copy");
        Assert.True(browseReplay.IsReplay);
        Assert.Contains("uncertain", browseReplay.ReplayDetail, StringComparison.OrdinalIgnoreCase);
        Assert.False(substringMatch.IsReplay);
        Assert.Equal(string.Empty, substringMatch.ReplayDetail);
        Assert.False(Assert.Single(workspace.Browse.Messages, row => row.MessageId == "replay-not-sent").IsReplay);
        Assert.False(Assert.Single(workspace.Browse.Messages, row => row.MessageId == "replay-other-profile").IsReplay);
        Assert.False(Assert.Single(workspace.Browse.Messages, row => row.MessageId == "replay-other-namespace").IsReplay);

        await workspace.Search.StartAsync("*", defaultMessageId: true);
        MessageRow searchReplay = Assert.Single(workspace.Search.Messages, row => row.MessageId == ReplayId);
        Assert.True(searchReplay.IsReplay);
        Assert.Contains("uncertain", searchReplay.ReplayDetail, StringComparison.OrdinalIgnoreCase);
        Assert.False(Assert.Single(workspace.Search.Messages, row => row.MessageId == ReplayId + "-ordinary-copy").IsReplay);

        string utc = browseReplay.EnqueuedDisplay;
        string replayDetailUtc = browseReplay.ReplayDetail;
        await workspace.ApplyPreferencesAsync(workspace.Preferences with
        {
            TimestampDisplay = TimestampDisplay.Local,
            DateFormat = DateDisplayFormat.Iso
        });
        Assert.True(browseReplay.IsReplay);
        Assert.NotEqual(utc, browseReplay.EnqueuedDisplay);
        Assert.NotEqual(replayDetailUtc, browseReplay.ReplayDetail);
        Assert.True(searchReplay.IsReplay);
        Assert.Contains("uncertain", searchReplay.ReplayDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_newly_saved_replay_is_badged_when_the_active_delivery_is_reloaded()
    {
        Fixture fixture = CreateFixture();
        await using InvestigationWorkspace workspace = fixture.Workspace;
        await workspace.InitializeAsync();
        await workspace.ConnectAsync();
        EntityNode subscription = Assert.Single(workspace.Browse.AllEntities(), node => node.Kind == nameof(EntityKind.Subscription));
        await workspace.Browse.SelectAsync(subscription, deadLetter: true);
        MessageRow original = Assert.Single(workspace.Browse.Messages, row => row.MessageId == "original-dlq-message");

        PreparedReplay prepared = workspace.PrepareReplay(original.Delivery);
        const string newReplayId = "replay-created-during-this-workspace";
        ReplayCopyOutcome outcome = await workspace.SendReplayAsync(prepared, newReplayId);
        Assert.Equal(ReplaySendStatus.Confirmed, outcome.Status);
        Assert.Contains(workspace.CurrentReplayHistory, attempt => attempt.Reservation.MessageId == newReplayId);

        await workspace.Browse.SelectAsync(subscription, deadLetter: false);
        MessageRow replayCopy = Assert.Single(workspace.Browse.Messages, row => row.MessageId == newReplayId);
        Assert.True(replayCopy.IsReplay);
        Assert.Contains("Sent", replayCopy.ReplayDetail, StringComparison.Ordinal);
        Assert.Contains("original-dlq-message", replayCopy.ReplayDetail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Replay_and_state_badges_and_history_toolbar_render_at_supported_widths(int width, int height) =>
        OnSta(() => ExerciseRenderedLayout(width, height));

    private static void ExerciseRenderedLayout(int width, int height)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        Fixture fixture = CreateFixture();
        var window = new InvestigationWindow(fixture.Workspace)
        {
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            PumpUntil(dispatcher, () => window.IsVisible, "Investigation to show");
            Complete(dispatcher, fixture.Workspace.InitializeAsync());
            Complete(dispatcher, fixture.Workspace.ConnectAsync());
            EntityNode topic = fixture.Workspace.Browse.AllEntities()
                .Single(node => node.Kind == nameof(EntityKind.Topic));
            Complete(dispatcher, fixture.Workspace.Browse.SelectAsync(topic, deadLetter: false));
            window.Width = width;
            window.Height = height;
            SettleLayout(dispatcher, window);

            var row = Assert.Single(fixture.Workspace.Browse.Messages, item => item.MessageId == ReplayId);
            Assert.True(row.IsReplay);
            CaptureIfEnabled(window, $"location37-{width}x{height}-active.png");
            var toolbarHistory = (Button)window.FindName("ReplayHistoryButton")!;
            var settings = (Button)window.FindName("SettingsButton")!;
            Assert.Equal("ReplayHistoryButton", System.Windows.Automation.AutomationProperties.GetAutomationId(toolbarHistory));
            System.Windows.Shapes.Path historyIcon = Assert.IsType<System.Windows.Shapes.Path>(
                FindDescendant<System.Windows.Shapes.Path>(toolbarHistory));
            Assert.Equal(window.FindResource("HistoryGeometry"), historyIcon.Data);
            AssertAdjacentBefore(toolbarHistory, settings);

            var messageGrid = (DataGrid)window.FindName("MessageGrid")!;
            AssertViewColumns(window, showsSource: true);
            var correlationValue = (TextBlock)window.FindName("CorrelationValue")!;
            var copyCorrelation = (Button)window.FindName("CopyCorrelationButton")!;
            Assert.Equal(row.CorrelationId, correlationValue.Text);
            Assert.Equal("Copy correlation ID", System.Windows.Automation.AutomationProperties.GetName(copyCorrelation));
            Assert.True(copyCorrelation.IsVisible);
            DataGridRow renderedRow = Assert.IsType<DataGridRow>(messageGrid.ItemContainerGenerator.ContainerFromItem(row));
            messageGrid.UpdateLayout();
            Border replayBadge = Assert.Single(Descendants(renderedRow).OfType<Border>(), border => border.Name == "ReplayBadge");
            Assert.Equal(row.ReplayDetail, replayBadge.ToolTip);
            Assert.True(replayBadge.Visibility == Visibility.Visible,
                $"Replay badge stayed {replayBadge.Visibility}; same row DataContext={ReferenceEquals(row, replayBadge.DataContext)}, "
                + $"context type={replayBadge.DataContext?.GetType().FullName}; detail={row.ReplayDetail}.");
            Assert.Contains(Descendants(replayBadge).OfType<TextBlock>(), block => block.Text == "Replay");

            var sourceColumn = messageGrid.Columns.Single(column => column.Header?.ToString() == "Location");
            Assert.Equal(Visibility.Visible, sourceColumn.Visibility);
            AssertLocationPresentation(window, messageGrid, renderedRow, row,
                EntityKind.Subscription, TopicName, SubscriptionName);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("InspectorState")!).Visibility);

            Rect historyBounds = toolbarHistory.TransformToAncestor(window).TransformBounds(new Rect(toolbarHistory.RenderSize));
            Rect settingsBounds = settings.TransformToAncestor(window).TransformBounds(new Rect(settings.RenderSize));
            Assert.True(historyBounds.Width > 0 && historyBounds.Right <= window.ActualWidth);
            Assert.True(settingsBounds.Width > 0 && settingsBounds.Right <= window.ActualWidth);
            Assert.True(renderedRow.ActualHeight > 0);

            EntityNode subscription = fixture.Workspace.Browse.AllEntities()
                .Single(node => node.Kind == nameof(EntityKind.Subscription));
            Complete(dispatcher, fixture.Workspace.Browse.SelectAsync(subscription, deadLetter: false));
            SettleLayout(dispatcher, window);
            AssertViewColumns(window, showsSource: false);
            MessageRow singleActiveReplay = Assert.Single(fixture.Workspace.Browse.Messages, item => item.MessageId == ReplayId);
            AssertRowCorrelationCopyInsideViewport(messageGrid, singleActiveReplay);
            Assert.Equal(row.CorrelationId, ((TextBlock)window.FindName("CorrelationValue")!).Text);
            CaptureIfEnabled(window, $"location37-{width}x{height}-single-active.png");
            fixture.Messages.AddDeadLetterReplay();
            Complete(dispatcher, fixture.Workspace.Browse.SelectAsync(subscription, deadLetter: true));
            SettleLayout(dispatcher, window);
            AssertViewColumns(window, showsSource: false);
            MessageRow singleDeadLetterReplay = Assert.Single(fixture.Workspace.Browse.Messages, item => item.MessageId == ReplayId);
            AssertRowCorrelationCopyInsideViewport(messageGrid, singleDeadLetterReplay);
            CaptureIfEnabled(window, $"location37-{width}x{height}-single-dlq.png");

            Complete(dispatcher, fixture.Workspace.Search.StartAsync("*", defaultMessageId: true));
            SettleLayout(dispatcher, window);
            AssertViewColumns(window, showsSource: true);
            CaptureIfEnabled(window, $"location37-{width}x{height}-search.png");
            var replayDlq = Assert.Single(fixture.Workspace.Search.Messages, item => item.MessageId == ReplayId && item.IsDeadLetter);
            Assert.True(replayDlq.IsReplay);
            // State must fit in the initial viewport; no horizontal scroll is allowed for this assertion.
            SettleLayout(dispatcher, window);
            DataGridRow dlqRow = Assert.IsType<DataGridRow>(messageGrid.ItemContainerGenerator.ContainerFromItem(replayDlq));
            Border stateBadge = Assert.Single(Descendants(dlqRow).OfType<Border>(), border => border.Name == "DeliveryStateBadge");
            Assert.True(stateBadge.IsVisible);
            Rect viewportBounds = stateBadge.TransformToAncestor(messageGrid).TransformBounds(new Rect(stateBadge.RenderSize));
            Assert.True(viewportBounds.Left >= 0 && viewportBounds.Right <= messageGrid.ActualWidth, "Delivery state must fit in the initial viewport.");
            DataGridCell cell = AncestorCell(stateBadge);
            Rect badgeBounds = stateBadge.TransformToAncestor(cell).TransformBounds(new Rect(stateBadge.RenderSize));
            Assert.True(badgeBounds.Left >= 0 && badgeBounds.Right <= cell.ActualWidth, "Long source must not clip the DLQ badge.");
            AssertLocationPresentation(window, messageGrid, dlqRow, replayDlq,
                EntityKind.Subscription, TopicName, SubscriptionName);
            CaptureIfEnabled(window, $"location37-{width}x{height}-dlq.png");

            ((Button)window.FindName("ClearSearchButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SettleLayout(dispatcher, window);
            Assert.False(fixture.Workspace.Search.IsActive);
            AssertViewColumns(window, showsSource: false);
            Complete(dispatcher, fixture.Workspace.Browse.SelectAsync(topic, deadLetter: false));
            SettleLayout(dispatcher, window);
            AssertViewColumns(window, showsSource: true);
        }
        finally
        {
            window.Close();
            Complete(dispatcher, fixture.Workspace.DisposeAsync().AsTask());
        }
    }

    private static DataGridCell AncestorCell(DependencyObject element)
    {
        for (DependencyObject? parent = element; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is DataGridCell cell) return cell;
        throw new InvalidOperationException("State badge has no cell.");
    }

    private static void AssertLocationPresentation(Window window, DataGrid grid, DataGridRow rowContainer,
        MessageRow row, EntityKind expectedKind, string? expectedTopic, string expectedName)
    {
        Assert.Equal(expectedKind, row.SourceKind);
        Assert.Equal(expectedTopic ?? string.Empty, row.SourceTopic);
        Assert.Equal(expectedName, row.SourceName);
        Assert.Equal(expectedKind == EntityKind.Subscription, row.IsSubscription);
        Assert.Equal(expectedTopic is null
            ? $"Queue: {expectedName}"
            : $"Topic: {expectedTopic}\nSubscription: {expectedName}", row.SourceDetail);

        DataGridColumn sourceColumn = grid.Columns.Single(column => column.Header?.ToString() == "Location");
        DataGridCell sourceCell = Assert.Single(Descendants(rowContainer).OfType<DataGridCell>(), cell => cell.Column == sourceColumn);
        FrameworkElement topicLine = Assert.IsAssignableFrom<FrameworkElement>(FindNamedDescendant(sourceCell, "SourceTopicLine"));
        FrameworkElement nameLine = Assert.IsAssignableFrom<FrameworkElement>(FindNamedDescendant(sourceCell, "SourceNameLine"));
        TextBlock topicText = Assert.IsType<TextBlock>(FindNamedDescendant(sourceCell, "SourceTopicText"));
        TextBlock nameText = Assert.IsType<TextBlock>(FindNamedDescendant(sourceCell, "SourceNameText"));
        System.Windows.Shapes.Path nameIcon = Assert.IsType<System.Windows.Shapes.Path>(FindNamedDescendant(sourceCell, "SourceNameIcon"));
        Assert.DoesNotContain(Descendants(sourceCell).OfType<Border>(), border => border.Name == "DeliveryStateBadge");
        var stateColumn = (DataGridColumn)window.FindName("StateColumn")!;
        DataGridCell stateCell = Assert.Single(Descendants(rowContainer).OfType<DataGridCell>(), cell => cell.Column == stateColumn);
        Border stateBadge = Assert.Single(Descendants(stateCell).OfType<Border>(), border => border.Name == "DeliveryStateBadge");

        Assert.Equal(expectedTopic is null ? Visibility.Collapsed : Visibility.Visible, topicLine.Visibility);
        Assert.Equal(Visibility.Visible, nameLine.Visibility);
        Assert.Equal(expectedName, nameText.Text);
        Assert.Equal(expectedTopic ?? string.Empty, topicText.Text);
        Assert.Equal(window.FindResource(expectedKind == EntityKind.Queue ? "QueueGeometry" : "SubscriptionGeometry"), nameIcon.Data);
        Assert.Equal(row.SourceDetail, nameText.ToolTip);
        if (expectedTopic is not null)
        {
            System.Windows.Shapes.Path topicIcon = Assert.IsType<System.Windows.Shapes.Path>(FindNamedDescendant(sourceCell, "SourceTopicIcon"));
            Assert.Equal(window.FindResource("TopicGeometry"), topicIcon.Data);
            Assert.Equal(row.SourceDetail, topicText.ToolTip);
            Assert.Equal(window.FindResource("SecondaryBrush"), topicText.Foreground);
            Assert.Equal(window.FindResource("InkBrush"), nameText.Foreground);
            Rect topicBounds = topicLine.TransformToAncestor(sourceCell).TransformBounds(new Rect(topicLine.RenderSize));
            Rect nameBounds = nameLine.TransformToAncestor(sourceCell).TransformBounds(new Rect(nameLine.RenderSize));
            Assert.True(topicBounds.Bottom <= nameBounds.Top + 1,
                $"Topic and subscription lines overlap: {topicBounds} / {nameBounds}.");
            Assert.True(nameBounds.Left > topicBounds.Left, "The subscription line should be indented beneath its topic.");
        }
        else
        {
            Assert.Equal(Visibility.Collapsed, topicLine.Visibility);
        }

        Rect cellBounds = stateCell.TransformToAncestor(grid).TransformBounds(new Rect(stateCell.RenderSize));
        Rect stateBounds = stateBadge.TransformToAncestor(grid).TransformBounds(new Rect(stateBadge.RenderSize));
        Rect sourceNameBounds = nameText.TransformToAncestor(grid).TransformBounds(new Rect(nameText.RenderSize));
        Assert.True(stateBadge.IsVisible && stateBounds.Left >= cellBounds.Left && stateBounds.Right <= cellBounds.Right,
            $"Delivery state badge escaped its State cell: {stateBounds} outside {cellBounds}.");
        Assert.True(stateBounds.Right <= sourceNameBounds.Left,
            $"The location label overlaps the pinned delivery state badge: {sourceNameBounds} / {stateBounds}.");
        Assert.Equal(row.StateLabel, Descendants(stateBadge).OfType<TextBlock>().Single().Text);
    }

    private static DependencyObject? FindNamedDescendant(DependencyObject root, string name) =>
        Descendants(root).FirstOrDefault(element => element is FrameworkElement framework && framework.Name == name);

    private static void AssertViewColumns(InvestigationWindow window, bool showsSource)
    {
        var source = (DataGridColumn)window.FindName("SourceColumn")!;
        var correlation = (DataGridColumn)window.FindName("CorrelationColumn")!;
        Assert.Equal(showsSource ? Visibility.Visible : Visibility.Collapsed, source.Visibility);
        Assert.Equal(showsSource ? Visibility.Collapsed : Visibility.Visible, correlation.Visibility);
    }

    private static void AssertRowCorrelationCopyInsideViewport(DataGrid grid, MessageRow row)
    {
        grid.UpdateLayout();
        var rowContainer = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(row));
        Button copyButton = Assert.Single(Descendants(rowContainer).OfType<Button>(), button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Copy row correlation ID");
        Assert.True(copyButton.IsVisible);
        Rect bounds = copyButton.TransformToAncestor(grid).TransformBounds(new Rect(copyButton.RenderSize));
        Assert.True(bounds.Width > 0 && bounds.Height > 0
            && bounds.Left >= 0 && bounds.Top >= 0
            && bounds.Right <= grid.ActualWidth && bounds.Bottom <= grid.ActualHeight,
            $"The row correlation Copy button is outside the message viewport at this width: {bounds} in {grid.ActualWidth}x{grid.ActualHeight}.");
    }

    private static void AssertAdjacentBefore(FrameworkElement first, FrameworkElement second)
    {
        DependencyObject parent = VisualTreeHelper.GetParent(first);
        Assert.Same(parent, VisualTreeHelper.GetParent(second));
        Assert.IsAssignableFrom<Panel>(parent);
        var children = ((Panel)parent).Children;
        Assert.Equal(children.IndexOf(first) + 1, children.IndexOf(second));
    }

    private static Fixture CreateFixture()
    {
        var profile = new InvestigationProfile(ProfileId, ConnectionProfileDefaults.LocalEmulator);
        string namespaceFingerprint = ReplayNamespace.Fingerprint(profile.Connection);
        string otherNamespaceFingerprint = Fingerprint("different-namespace");
        var source = new EntityAddress(EntityKind.Subscription, SubscriptionName, TopicName);
        ReplayFamilyState family = new(Guid.NewGuid(), Fingerprint("replay-root"), "original-dlq-message", source, 1);
        ReplayAttempt Attempt(string id, string attemptProfile, string fingerprint, ReplaySendStatus status) =>
            new(Guid.NewGuid(), attemptProfile, fingerprint, new ReplayReservation(family, id), source,
                52, Fingerprint("original-fingerprint"), "original-dlq-message", DateTimeOffset.UtcNow,
                status, status == ReplaySendStatus.Confirmed ? DateTimeOffset.UtcNow : null);

        var attempts = new[]
        {
            Attempt(ReplayId, ProfileId, namespaceFingerprint, ReplaySendStatus.Uncertain),
            Attempt("replay-not-sent", ProfileId, namespaceFingerprint, ReplaySendStatus.NotSent),
            Attempt("replay-other-profile", "another-profile", namespaceFingerprint, ReplaySendStatus.Confirmed),
            Attempt("replay-other-namespace", ProfileId, otherNamespaceFingerprint, ReplaySendStatus.Confirmed)
        };
        var preferences = new WorkspacePreferences
        {
            Profiles = [profile],
            SelectedProfileId = ProfileId,
            ReplayAttempts = attempts,
            LogExpanded = false
        };
        var topicNode = new ServiceBusEntityNode(EntityKind.Topic, TopicName, null,
            new EntityRuntimeCounts(0, 0, 0, 0), new EntityMetadata(TopicName, "Active", null, null, null, null, null, null, null));
        var subscriptionNode = new ServiceBusEntityNode(EntityKind.Subscription, SubscriptionName, TopicName,
            new EntityRuntimeCounts(0, 4, 0, 4), new EntityMetadata($"{TopicName}/subscriptions/{SubscriptionName}",
                "Active", null, null, null, null, null, null, null));
        var snapshot = new EntityDiscoverySnapshot(
            new[] { topicNode, subscriptionNode }.Select(node => new EntityObservation(node, new EntityCountObservation(
                new(null, CountAvailability.Known), new(null, CountAvailability.Known), new(null, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow, true, []);
        var messages = new FakeMessages(source);
        var connections = new BrokerConnectionWorkflow(() => new FakeFactory(), _ => new FakeBrowser(snapshot), _ => messages);
        var workspace = new InvestigationWorkspace(new FakeStore(preferences), connections,
            _ => new FakeReplaySender(messages));
        return new Fixture(workspace, messages);
    }

    private static string Fingerprint(string input) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject =>
        Descendants(root).OfType<T>().FirstOrDefault();

    private static void CaptureIfEnabled(Window window, string filename)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;
        var content = (FrameworkElement)window.Content;
        SettleLayout(window.Dispatcher, content);
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))
            && !File.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("The repository root was not found for screenshot output.");
        string output = Path.Combine(directory.FullName, "artifacts", "location37-proof");
        Directory.CreateDirectory(output);
        WpfScreenshot.SaveWindowContent(window, Path.Combine(output, filename),
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), minimumBytes: 1000);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Replay badge WPF proof exceeded its 60-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Complete(Dispatcher dispatcher, Task task)
    {
        PumpUntil(dispatcher, () => task.IsCompleted, "workspace operation to complete");
        task.GetAwaiter().GetResult();
    }

    private static void SettleLayout(Dispatcher dispatcher, FrameworkElement element)
    {
        var operation = dispatcher.InvokeAsync(element.UpdateLayout, DispatcherPriority.ApplicationIdle);
        PumpUntil(dispatcher, () => operation.Task.IsCompleted, "WPF layout to settle");
        operation.Task.GetAwaiter().GetResult();
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

    private sealed record Fixture(InvestigationWorkspace Workspace, FakeMessages Messages);

    private sealed class FakeStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages(EntityAddress source) : IServiceBusMessageService
    {
        private readonly List<ExplorerMessage> values =
        [
            Message(ReplayId, 51),
            Message(ReplayId + "-ordinary-copy", 52),
            Message("replay-not-sent", 53),
            Message("replay-other-profile", 54),
            Message("replay-other-namespace", 55)
        ];
        private readonly List<ExplorerMessage> deadLetters = [Message("original-dlq-message", 11)];

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            IEnumerable<ExplorerMessage> sourceMessages = bucket == MessageBucket.Active ? values : deadLetters;
            IReadOnlyList<ExplorerMessage> result = address == source
                ? sourceMessages.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                    .Take(take).ToArray() : [];
            return Task.FromResult(result);
        }

        public void AddDeadLetterReplay() => deadLetters.Add(Message(ReplayId, 12));
        public void AddReplay(string id) => values.Add(Message(id, values.Max(message => message.SequenceNumber) + 1));

        private static ExplorerMessage Message(string id, long sequence) => new(id, sequence, "{}", id, 2,
            new DateTimeOffset(2026, 10, 1, 0, 30, 0, TimeSpan.Zero), null, 0, "application/json", "correlation-" + id,
            null, "OrderPlaced", new Dictionary<string, object?>(), new Dictionary<string, object?>());

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeReplaySender(FakeMessages messages) : IReplayCopySender
    {
        public Task SendAsync(EntityAddress destination, Azure.Messaging.ServiceBus.ServiceBusMessage message,
            CancellationToken cancellationToken)
        {
            messages.AddReplay(message.MessageId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
