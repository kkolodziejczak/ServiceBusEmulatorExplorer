using System.Runtime.ExceptionServices;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationWindowRenderTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Investigation_window_renders_active_badges_and_long_topic_content_at_supported_sizes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderWindow();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The WPF render proof exceeded its 30-second bound.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RenderWindow()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        InvestigationWorkspace workspace = CreateWorkspace();
        InvestigationWindow window = new(workspace);
        try
        {
            window.Show();
            PumpUntil(dispatcher, () => window.IsVisible && window.ActualWidth > 0 && window.ActualHeight > 0,
                TimeSpan.FromSeconds(5));

            Task connect = workspace.ConnectAsync();
            PumpUntil(dispatcher, () => connect.IsCompleted, TimeSpan.FromSeconds(5));
            connect.GetAwaiter().GetResult();
            AssertActivityPresentation(window, workspace, dispatcher);
            EntityNode topic = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Topic));
            Task select = workspace.Browse.SelectAsync(topic, deadLetter: false);
            PumpUntil(dispatcher, () => select.IsCompleted, TimeSpan.FromSeconds(5));
            select.GetAwaiter().GetResult();

            ToggleButton deadLetterTab = (ToggleButton)window.FindName("DeadLetterTab")!;
            ToggleThroughAutomation(deadLetterTab);
            PumpUntil(dispatcher, () => workspace.Browse.IsDeadLetter && !workspace.Browse.IsBusy,
                TimeSpan.FromSeconds(5));
            Assert.True(workspace.Browse.IsDeadLetter);

            ToggleButton activeTab = (ToggleButton)window.FindName("ActiveTab")!;
            ToggleThroughAutomation(activeTab);
            PumpUntil(dispatcher, () => !workspace.Browse.IsDeadLetter && !workspace.Browse.IsBusy,
                TimeSpan.FromSeconds(5));
            Assert.False(workspace.Browse.IsDeadLetter);
            ToggleThroughAutomation(activeTab);
            Assert.True(activeTab.IsChecked);
            Assert.False(workspace.Browse.IsDeadLetter);

            ToggleButton rawTab = (ToggleButton)window.FindName("RawTab")!;
            ToggleButton propertiesTab = (ToggleButton)window.FindName("PropertiesTab")!;
            ToggleButton jsonTab = (ToggleButton)window.FindName("JsonTab")!;
            RichTextBox bodyViewer = (RichTextBox)window.FindName("BodyViewer")!;
            ToggleThroughAutomation(rawTab);
            Assert.Equal(Visibility.Visible, bodyViewer.Visibility);
            Assert.Equal(workspace.Inspector.RawText.TrimEnd(),
                new System.Windows.Documents.TextRange(bodyViewer.Document.ContentStart, bodyViewer.Document.ContentEnd).Text.TrimEnd());
            ToggleThroughAutomation(propertiesTab);
            Assert.False(rawTab.IsChecked);
            Assert.Equal(workspace.Inspector.PropertiesText.ReplaceLineEndings("\n").TrimEnd(),
                new System.Windows.Documents.TextRange(bodyViewer.Document.ContentStart, bodyViewer.Document.ContentEnd).Text.ReplaceLineEndings("\n").TrimEnd());
            ToggleThroughAutomation(jsonTab);
            Assert.False(propertiesTab.IsChecked);
            Assert.Equal(Visibility.Collapsed, bodyViewer.Visibility);
            ToggleThroughAutomation(jsonTab);
            Assert.True(jsonTab.IsChecked);

            CheckBox selectAll = (CheckBox)window.FindName("SelectAllBox")!;
            Assert.False(selectAll.IsChecked);
            workspace.Browse.Messages[0].IsSelected = true;
            Assert.Null(selectAll.IsChecked);
            workspace.Browse.SetAllChecked(true);
            Assert.True(selectAll.IsChecked);
            workspace.Browse.SetAllChecked(false);
            Assert.False(selectAll.IsChecked);
            workspace.Browse.Messages[0].IsSelected = true;
            workspace.Browse.Messages[1].IsSelected = false;
            workspace.Browse.FocusedMessage = workspace.Browse.Messages[0];
            window.UpdateLayout();
            Border inspectorState = (Border)window.FindName("InspectorState")!;
            FrameworkElement inspectorHeading = (FrameworkElement)window.FindName("InspectorHeading")!;
            FrameworkElement inspectorTabs = (FrameworkElement)window.FindName("InspectorTabs")!;
            DataGrid messageGrid = (DataGrid)window.FindName("MessageGrid")!;

            foreach ((double width, double height, string name) in new[]
            {
                (1500, 1000, "desktop"),
                (1100, 800, "compact"),
                (980, 640, "minimum")
            })
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                Task settled = dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle).Task;
                PumpUntil(dispatcher, () => settled.IsCompleted, TimeSpan.FromSeconds(5));
                ExerciseWatchControls(window, workspace, dispatcher, topic, name);
                CaptureIfEnabled(window, name);
                AssertActiveBadge(inspectorState, expectedBorder: "#6C9BD2");
                AssertVisibleBounds(window, inspectorHeading, inspectorTabs, messageGrid);

                DataGridRow selectedRow = GetRow(messageGrid, 0);
                Assert.True(selectedRow.IsSelected, "The selected message row was not selected in the rendered grid.");
                AssertActiveBadge(FindActiveBadge(selectedRow)
                    ?? throw new Xunit.Sdk.XunitException("The selected Active row badge was not rendered."),
                    expectedBorder: "#6C9BD2");
                // The compact list may realize only one row. Verify before scrolling recycles its container.
                DataGridRow unselectedRow = GetRow(messageGrid, 1);
                Assert.False(unselectedRow.IsSelected, "The unselected message row was selected in the rendered grid.");
                AssertActiveBadge(FindActiveBadge(unselectedRow)
                    ?? throw new Xunit.Sdk.XunitException("The unselected Active row badge was not rendered."),
                    expectedBorder: "#6C9BD2");
                CaptureIfEnabled(window, name + "-unselected");
                messageGrid.ScrollIntoView(messageGrid.Items[0]);
            }
            ExerciseNotificationRecovery(window, workspace, dispatcher);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
                PumpUntil(dispatcher, () => !window.IsVisible, TimeSpan.FromSeconds(5));
            }

            if (window.IsVisible)
            {
                window.Hide();
            }

            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void AssertActivityPresentation(InvestigationWindow window, InvestigationWorkspace workspace, Dispatcher dispatcher)
    {
        workspace.Activity.Clear();
        var instant = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
        workspace.Activity.Add(new(instant, "Connected to verification fixture.", false));
        workspace.Activity.Add(new(instant, "Verification warning.", true));
        workspace.Activity.Add(new(instant, "Baseline ready.", false, Watch: true));
        RichTextBox log = (RichTextBox)window.FindName("LogText")!;
        var paragraphs = log.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToArray();
        var info = paragraphs[0].Inlines.OfType<System.Windows.Documents.Run>().ToArray();
        var warning = paragraphs[1].Inlines.OfType<System.Windows.Documents.Run>().ToArray();
        Assert.Equal(3, info.Length);
        Assert.Equal("09:00:00 UTC   ", info[0].Text);
        Assert.Equal("#87A7BF", BrushHex(info[0].Foreground));
        Assert.Equal("INFO    ", info[1].Text);
        Assert.Equal("#90EE90", BrushHex(info[1].Foreground));
        Assert.Equal("WARN    ", warning[1].Text);
        Assert.Equal("#FFA500", BrushHex(warning[1].Foreground));
        Assert.Equal("Verification warning.", warning[2].Text);
        var watch = paragraphs[2].Inlines.OfType<System.Windows.Documents.Run>().ToArray();
        Assert.Equal("WATCH   ", watch[1].Text);
        Assert.Equal("#00FFFF", BrushHex(watch[1].Foreground));
        Assert.Equal("09:00:00 UTC", ((TextBlock)window.FindName("LastOperationTime")!).Text);
        Task local = workspace.UpdateDisplayPreferencesAsync(timestampDisplay: TimestampDisplay.Local);
        PumpUntil(dispatcher, () => local.IsCompleted, TimeSpan.FromSeconds(5));
        local.GetAwaiter().GetResult();
        var localTime = (System.Windows.Documents.Run)((System.Windows.Documents.Paragraph)log.Document.Blocks.FirstBlock).Inlines.FirstInline;
        Assert.Equal(instant.ToLocalTime().ToString("HH:mm:ss") + " Local   ", localTime.Text);
        Assert.Equal(instant, workspace.Activity[0].TimestampUtc);
        Task utc = workspace.UpdateDisplayPreferencesAsync(timestampDisplay: TimestampDisplay.Utc);
        PumpUntil(dispatcher, () => utc.IsCompleted, TimeSpan.FromSeconds(5));
        utc.GetAwaiter().GetResult();
    }

    private static void ExerciseNotificationRecovery(InvestigationWindow window, InvestigationWorkspace workspace, Dispatcher dispatcher)
    {
        void Await(Task operation)
        {
            PumpUntil(dispatcher, () => operation.IsCompleted, TimeSpan.FromSeconds(5));
            operation.GetAwaiter().GetResult();
        }
        WatchNotificationWindow? Notification() => PresentationSource.CurrentSources.Cast<PresentationSource>()
            .Select(source => source.RootVisual).OfType<WatchNotificationWindow>().SingleOrDefault(notification => notification.IsVisible);
        void Invoke(WatchNotificationWindow notification, string id)
        {
            Button button = FindNotificationButtons(notification).Single(candidate => System.Windows.Automation.AutomationProperties.GetAutomationId(candidate) == id);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        }

        Await(workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, [new(WatchScopeResolver.ConnectionScopeKey, true, false)]));
        var available = workspace.Browse.Messages[0].Delivery;
        var missing = new MessageDelivery(available.Identity with { SequenceNumber = 999 },
            available.Message with { SequenceNumber = 999, MessageId = "expired-watch-message", CorrelationId = "unavailable-watch-case" });
        workspace.Watch.PendingArrivals.Add(available);
        workspace.Watch.PendingArrivals.Add(missing);
        PumpUntil(dispatcher, () => Notification() is not null, TimeSpan.FromSeconds(5));
        window.Hide();
        Invoke(Notification()!, "InvestigateWatchNotification");
        PumpUntil(dispatcher, () => window.IsVisible && workspace.Search.IsActive && !workspace.Search.IsBusy
            && workspace.Watch.PendingArrivals.Count == 1 && Notification()?.IsEnabled == true, TimeSpan.FromSeconds(5));
        Assert.Equal(missing, Assert.Single(workspace.Watch.PendingArrivals));
        Assert.Contains(workspace.Activity, entry => entry.Message.Contains("not found in the scanned results", StringComparison.Ordinal));

        Await(workspace.DisconnectAsync());
        Assert.NotNull(Notification());
        Invoke(Notification()!, "InvestigateWatchNotification");
        PumpUntil(dispatcher, () => workspace.Activity.Any(entry => entry.Message.StartsWith("Reconnect to investigate", StringComparison.Ordinal)), TimeSpan.FromSeconds(5));
        Assert.Contains(workspace.Activity, entry => entry.Message.StartsWith("Reconnect to investigate", StringComparison.Ordinal));
        Assert.Single(workspace.Watch.PendingArrivals);
        Await(workspace.ConnectAsync());
        Assert.Single(workspace.Watch.PendingArrivals);

        var changedProfiles = workspace.Preferences.Profiles.Select(profile => profile with { ColorHex = "#7540BF" }).ToArray();
        Await(workspace.ApplyPreferencesAsync(workspace.Preferences with { Profiles = changedProfiles }));
        Assert.Equal(((SolidColorBrush)window.FindResource("PrimaryBrush")).Color,
            ((SolidColorBrush)Notification()!.FindResource("PrimaryBrush")).Color);
        Await(workspace.ApplyPreferencesAsync(workspace.Preferences with { NotificationsEnabled = false }));
        Assert.Null(Notification());
        Assert.Single(workspace.Watch.PendingArrivals);
        Await(workspace.ApplyPreferencesAsync(workspace.Preferences with { NotificationsEnabled = true }));
        PumpUntil(dispatcher, () => Notification() is not null, TimeSpan.FromSeconds(5));
        Invoke(Notification()!, "DismissWatchNotification");
        PumpUntil(dispatcher, () => workspace.Watch.PendingArrivals.Count == 0, TimeSpan.FromSeconds(5));
        Assert.Empty(workspace.Watch.PendingArrivals);
        Assert.Null(Notification());
        Await(workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, []));
        workspace.Search.Clear();
    }

    private static IEnumerable<Button> FindNotificationButtons(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button) yield return button;
            foreach (var descendant in FindNotificationButtons(child)) yield return descendant;
        }
    }

    private static void ExerciseWatchControls(InvestigationWindow window, InvestigationWorkspace workspace,
        Dispatcher dispatcher, EntityNode topic, string size)
    {
        void Invoke(string name) => ((IInvokeProvider)new ButtonAutomationPeer((Button)window.FindName(name))
            .GetPattern(PatternInterface.Invoke)!).Invoke();
        void Settle()
        {
            Task idle = dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle).Task;
            PumpUntil(dispatcher, () => idle.IsCompleted, TimeSpan.FromSeconds(5));
        }
        Invoke("GlobalWatchButton");
        Settle();
        var dialog = window.OwnedWindows.OfType<GlobalWatchWindow>().Single();
        ToggleThroughAutomation((CheckBox)dialog.FindName("GlobalActiveChoice"));
        Settle();
        Assert.Contains(workspace.Preferences.Watches[workspace.SelectedProfile.Id], rule =>
            rule.ScopeKey == WatchScopeResolver.ConnectionScopeKey && rule.Active == true);
        Assert.Equal("Watching", ((TextBlock)window.FindName("WatchButtonLabel")).Text);
        Assert.True(((Button)window.FindName("WatchSummaryButton")).IsVisible);

        Invoke("WatchButton");
        Settle();
        var popup = (Popup)window.FindName("WatchPopup");
        Assert.True(popup.IsOpen);
        var active = (CheckBox)window.FindName("WatchActiveChoice");
        var dlq = (CheckBox)window.FindName("WatchDlqChoice");
        Assert.True(active.IsChecked);
        Assert.False(dlq.IsChecked);
        ToggleThroughAutomation(dlq);
        Settle();
        Assert.True(new WatchRuleEditor(workspace.Browse.Roots,
            workspace.Preferences.Watches[workspace.SelectedProfile.Id]).IsWatched(topic, true));
        Invoke("WatchButton");
        Settle();
        CaptureIfEnabled((FrameworkElement)popup.Child, size + "-watch-popup");
        CaptureIfEnabled(window, size + "-watch-enabled");

        // A modeless dialog must retain the topic choice made through the main window.
        ToggleThroughAutomation((CheckBox)dialog.FindName("GlobalActiveChoice"));
        Settle();
        var editor = new WatchRuleEditor(workspace.Browse.Roots, workspace.Preferences.Watches[workspace.SelectedProfile.Id]);
        Assert.False(editor.GlobalActive);
        Assert.True(editor.IsWatched(topic, true));
        Invoke("WatchSummaryButton");
        Settle();
        ContextMenu overview = ((Button)window.FindName("WatchSummaryButton")).ContextMenu;
        Assert.True(overview.IsOpen);
        Assert.Equal(2, overview.Items.OfType<MenuItem>().Count(item => item.IsCheckable));
        CaptureIfEnabled(overview, size + "-watch-overview");
        overview.IsOpen = false;
        Invoke("WatchButton");
        Settle();
        Invoke("StopWatchingButton");
        Settle();
        Assert.False(popup.IsOpen);
        Assert.Equal("Watch", ((TextBlock)window.FindName("WatchButtonLabel")).Text);
        dialog.Close();
        var clear = workspace.UpdateWatchRulesAsync(workspace.SelectedProfile.Id, []);
        PumpUntil(dispatcher, () => clear.IsCompleted, TimeSpan.FromSeconds(5));
        clear.GetAwaiter().GetResult();
        Assert.False(((Button)window.FindName("WatchSummaryButton")).IsVisible);
    }

    private static InvestigationWorkspace CreateWorkspace()
    {
        var preferences = new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile(
                "render-proof",
                new ConnectionProfile("Render proof", "runtime", "admin"))],
            SelectedProfileId = "render-proof",
            WindowWidth = 1500,
            WindowHeight = 1000,
            WasConnected = false
        };
        var store = new FakeStore(preferences);
        var workflow = new BrokerConnectionWorkflow(
            () => new FakeFactory(),
            _ => new FakeBrowser(CreateSnapshot()),
            _ => new FakeMessages());
        return new InvestigationWorkspace(store, workflow);
    }

    private static EntityDiscoverySnapshot CreateSnapshot()
    {
        ServiceBusEntityNode topic = CreateEntity(EntityKind.Topic, "orders", topicName: null, active: 110, deadLetter: 0);
        ServiceBusEntityNode billing = CreateEntity(EntityKind.Subscription, "billing", "orders", active: 55, deadLetter: 0);
        ServiceBusEntityNode audit = CreateEntity(EntityKind.Subscription, "audit", "orders", active: 55, deadLetter: 0);
        return new(
            new[] { topic, billing, audit }.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);
    }

    private static ServiceBusEntityNode CreateEntity(
        EntityKind kind,
        string name,
        string? topicName,
        long active,
        long deadLetter) =>
        new(
            kind,
            name,
            topicName,
            new EntityRuntimeCounts(active, deadLetter, 0, active + deadLetter),
            new EntityMetadata(
                topicName is null ? name : $"{topicName}/subscriptions/{name}",
                "Active",
                null,
                null,
                null,
                null,
                null,
                null,
                null));

    private static void AssertActiveBadge(Border badge, string expectedBorder)
    {
        Assert.Equal("#EFF6FF", BrushHex(badge.Background));
        Assert.Equal(expectedBorder, BrushHex(badge.BorderBrush));
        TextBlock text = FindTextBlock(badge, "Active")
            ?? throw new Xunit.Sdk.XunitException("The Active badge text was not rendered.");
        Assert.Equal("#174A7E", BrushHex(text.Foreground));
        Assert.Equal(1d, badge.BorderThickness.Left);
        Assert.Equal(3d, badge.CornerRadius.TopLeft);
    }

    private static string BrushHex(Brush brush) => brush is SolidColorBrush solid
        ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
        : throw new Xunit.Sdk.XunitException("Expected a solid badge brush.");

    private static Border? FindActiveBadge(DependencyObject root)
    {
        if (root is Border { Child: TextBlock { Text: "Active" } } border)
        {
            return border;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindActiveBadge(VisualTreeHelper.GetChild(root, index)) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string text)
    {
        if (root is TextBlock block && string.Equals(block.Text, text, StringComparison.Ordinal))
        {
            return block;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindTextBlock(VisualTreeHelper.GetChild(root, index), text) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static DataGridRow GetRow(DataGrid grid, int index)
    {
        grid.ScrollIntoView(grid.Items[index]);
        grid.UpdateLayout();
        return grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow
            ?? throw new Xunit.Sdk.XunitException($"The message row at index {index} was not realized.");
    }

    private static void AssertVisibleBounds(Window window, params FrameworkElement[] elements)
    {
        foreach (FrameworkElement element in elements)
        {
            Assert.NotEqual(Visibility.Collapsed, element.Visibility);
            Rect bounds = element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Assert.True(bounds.Left >= -1 && bounds.Top >= -1,
                $"{element.Name} starts outside the window at {bounds}.");
            Assert.True(bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1,
                $"{element.Name} is clipped by the window at {bounds}.");
        }
    }

    private static void CaptureIfEnabled(FrameworkElement window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, $"investigation-{name}.png"));
        encoder.Save(output);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The WPF render proof did not reach its expected state.");
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void ToggleThroughAutomation(ToggleButton toggleButton)
    {
        AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(toggleButton)
            ?? throw new Xunit.Sdk.XunitException($"No automation peer was created for {toggleButton.Name}.");
        IToggleProvider provider = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider
            ?? throw new Xunit.Sdk.XunitException($"The {toggleButton.Name} automation peer has no Toggle pattern.");
        provider.Toggle();
    }

    private sealed class FakeStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;

        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            if (bucket == MessageBucket.DeadLetter)
            {
                return Task.FromResult<IReadOnlyList<ExplorerMessage>>([]);
            }

            var messages = Enumerable.Range(1, 55)
                .Select(sequence => CreateMessage(address.Name, sequence))
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToArray();
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(messages);
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        private static ExplorerMessage CreateMessage(string subscription, int sequence)
        {
            string body = "{\"event\":\"order.created\",\"subscription\":\""
                + subscription
                + "\",\"sequence\":"
                + sequence
                + ",\"details\":\""
                + new string('x', 2800)
                + "\"}";
            return new ExplorerMessage(
                $"message-{subscription}-{sequence}",
                sequence,
                body,
                body,
                System.Text.Encoding.UTF8.GetByteCount(body),
                DateTimeOffset.UtcNow,
                null,
                0,
                "application/json",
                $"correlation-{subscription}",
                null,
                "Order created",
                new Dictionary<string, object?> { ["subscription"] = subscription },
                new Dictionary<string, object?>())
            {
                RawBody = BinaryData.FromString(body)
            };
        }
    }
}
