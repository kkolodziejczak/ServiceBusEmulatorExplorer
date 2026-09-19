using System.IO;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationSearchRenderTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Search_partial_continue_stop_and_clear_render_truthfully_at_supported_sizes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderAndInteract();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The search render proof exceeded its 30-second bound.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Search_empty_invalid_suggestions_and_discovery_failure_render_truthfully()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderEmptyAndSuggestionStates();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The empty search render proof exceeded its 30-second bound.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RenderEmptyAndSuggestionStates()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var messages = new FakeMessages();
        InvestigationWorkspace workspace = CreateWorkspace(messages, searchDeliveryBudget: 10_000);
        var window = new InvestigationWindow(workspace);
        workspace.ConfirmDiscard = () => Task.FromResult(true);

        try
        {
            ShowAndConnect(dispatcher, workspace, window);
            TextBox searchBox = (TextBox)window.FindName("SearchBox")!;
            Button clearButton = (Button)window.FindName("ClearSearchButton")!;
            Popup suggestionsPopup = (Popup)window.FindName("SuggestionsPopup")!;
            ListBox suggestions = (ListBox)window.FindName("SuggestionsList")!;
            TextBlock emptyMessage = (TextBlock)window.FindName("EmptyMessage")!;
            TextBlock emptyDescription = (TextBlock)window.FindName("EmptyDescription")!;
            StackPanel emptyResults = (StackPanel)window.FindName("EmptyResults")!;

            searchBox.Focus();
            searchBox.Text = "\"unterminated";
            window.UpdateLayout();
            Assert.True(suggestionsPopup.IsOpen, "Invalid search text should still open grouped suggestions.");
            Assert.True(suggestions.Items.Count >= 2, "The suggestion popup should render the two global search actions.");
            Assert.True(suggestions.ItemsSource is ICollectionView view && view.Groups.Count > 0,
                "Suggestions must retain their grouped presentation.");

            RaisePreviewKey(searchBox, Key.Down);
            Assert.Equal(0, suggestions.SelectedIndex);
            RaisePreviewKey(searchBox, Key.Enter);
            PumpUntil(dispatcher, () => emptyResults.Visibility == Visibility.Visible &&
                emptyMessage.Text.Contains("Invalid", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5));
            Assert.True(workspace.Search.IsActive, "An invalid query should retain the search surface so its error is visible.");
            Assert.False(workspace.Search.CanContinue, "An invalid query must not offer Continue.");
            Assert.Contains("Close the quoted ID", emptyDescription.Text, StringComparison.OrdinalIgnoreCase);

            foreach ((double width, double height, string name) in new[]
            {
                (1500, 1000, "search-invalid-desktop"),
                (1100, 800, "search-invalid-compact"),
                (980, 640, "search-invalid-minimum")
            })
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                AssertVisibleBounds(window, emptyResults, emptyMessage, emptyDescription);
                FrameworkElement listContent = (FrameworkElement)emptyResults.Parent;
                foreach (FrameworkElement content in new FrameworkElement[]
                    { emptyMessage, emptyDescription, (Button)window.FindName("EmptyClearButton")! })
                {
                    Rect localBounds = content.TransformToAncestor(listContent)
                        .TransformBounds(new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    Assert.True(localBounds.Top >= -1 && localBounds.Bottom <= listContent.ActualHeight + 1,
                        $"{content.Name} is clipped by the list pane: {localBounds}, height {listContent.ActualHeight}.");
                }
                CaptureIfEnabled(window, name);
            }

            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(dispatcher, () => !suggestionsPopup.IsOpen && searchBox.IsKeyboardFocusWithin,
                TimeSpan.FromSeconds(5));
            Assert.Equal(string.Empty, searchBox.Text);
            Assert.Equal(Visibility.Collapsed, clearButton.Visibility);

            searchBox.Text = "does-not-exist";
            suggestionsPopup.IsOpen = false;
            Task emptySearch = workspace.Search.StartAsync("does-not-exist", defaultMessageId: true);
            PumpUntil(dispatcher, () => emptySearch.IsCompleted && workspace.Search.IsActive && !workspace.Search.IsBusy,
                TimeSpan.FromSeconds(5));
            emptySearch.GetAwaiter().GetResult();
            window.UpdateLayout();
            TextBlock completedEmptyMessage = (TextBlock)window.FindName("EmptyMessage")!;
            TextBlock completedEmptyDescription = (TextBlock)window.FindName("EmptyDescription")!;
            StackPanel completedEmptyResults = (StackPanel)window.FindName("EmptyResults")!;
            Border searchStatusPanel = (Border)window.FindName("SearchStatusPanel")!;
            TextBlock searchStatus = (TextBlock)window.FindName("SearchStatusText")!;
            Assert.Equal(Visibility.Visible, completedEmptyResults.Visibility);
            Assert.Contains("No matching", completedEmptyMessage.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Visibility.Visible, searchStatusPanel.Visibility);
            Assert.Contains("complete", searchStatus.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(workspace.Search.Status, completedEmptyDescription.Text);

            ClearSearch((Button)window.FindName("ClearSearchButton")!);
        }
        finally
        {
            CloseWindow(dispatcher, window);
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        var incompleteMessages = new FakeMessages();
        InvestigationWorkspace incompleteWorkspace = CreateWorkspace(
            incompleteMessages,
            searchDeliveryBudget: 10_000,
            snapshot: CreateSnapshot() with
            {
                IsComplete = false,
                Issues = ["Subscription discovery failed for events."]
            });
        var incompleteWindow = new InvestigationWindow(incompleteWorkspace);
        incompleteWorkspace.ConfirmDiscard = () => Task.FromResult(true);
        try
        {
            ShowAndConnect(dispatcher, incompleteWorkspace, incompleteWindow);
            Task incompleteSearch = incompleteWorkspace.Search.StartAsync("does-not-exist", defaultMessageId: true);
            PumpUntil(dispatcher, () => incompleteSearch.IsCompleted && incompleteWorkspace.Search.IsActive && !incompleteWorkspace.Search.IsBusy,
                TimeSpan.FromSeconds(5));
            incompleteSearch.GetAwaiter().GetResult();
            incompleteWindow.UpdateLayout();
            TextBlock failureStatus = (TextBlock)incompleteWindow.FindName("SearchStatusText")!;
            TextBlock failureEmptyDescription = (TextBlock)incompleteWindow.FindName("EmptyDescription")!;
            Assert.Contains("discovery was incomplete", failureStatus.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("discovery was incomplete", failureEmptyDescription.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Visibility.Visible, ((Border)incompleteWindow.FindName("SearchStatusPanel")!).Visibility);
        }
        finally
        {
            CloseWindow(dispatcher, incompleteWindow);
            incompleteWorkspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void RenderAndInteract()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var messages = new FakeMessages();
        InvestigationWorkspace workspace = CreateWorkspace(messages);
        var window = new InvestigationWindow(workspace);
        workspace.ConfirmDiscard = () => Task.FromResult(true);

        try
        {
            window.Show();
            PumpUntil(dispatcher, () => window.IsVisible && window.ActualWidth > 0 && window.ActualHeight > 0,
                TimeSpan.FromSeconds(5));

            Task connect = workspace.ConnectAsync();
            PumpUntil(dispatcher, () => connect.IsCompleted, TimeSpan.FromSeconds(5));
            connect.GetAwaiter().GetResult();

            EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Kind == nameof(EntityKind.Queue));
            Task select = workspace.Browse.SelectAsync(queue, deadLetter: false);
            PumpUntil(dispatcher, () => select.IsCompleted, TimeSpan.FromSeconds(5));
            select.GetAwaiter().GetResult();
            MessageRow browseFocus = workspace.Browse.Messages[0];
            workspace.Browse.FocusedMessage = browseFocus;
            workspace.Inspector.Document.Replace(0, workspace.Inspector.Document.TextLength, "{\"draft\":true}");
            Assert.True(workspace.Inspector.HasDrafts, "The browse fixture must contain a draft before search starts.");

            TextBox searchBox = (TextBox)window.FindName("SearchBox")!;
            Border searchStatusPanel = (Border)window.FindName("SearchStatusPanel")!;
            TextBlock searchStatus = (TextBlock)window.FindName("SearchStatusText")!;
            Button continueButton = (Button)window.FindName("LoadMoreButton")!;
            Button stopButton = (Button)window.FindName("StopSearchButton")!;
            Button clearButton = (Button)window.FindName("ClearSearchButton")!;
            DataGrid messageGrid = (DataGrid)window.FindName("MessageGrid")!;

            searchBox.Text = "*";
            Task start = workspace.Search.StartAsync("*", defaultMessageId: true);
            PumpUntil(dispatcher, () => start.IsCompleted && workspace.Search.IsActive && !workspace.Search.IsBusy,
                TimeSpan.FromSeconds(5));
            start.GetAwaiter().GetResult();
            window.UpdateLayout();

            Assert.Equal("Continue", continueButton.Content);
            Assert.True(workspace.Search.CanContinue, "The tiny first search budget must leave a resumable partial search.");
            Assert.Contains("paused", workspace.Search.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Visibility.Visible, searchStatusPanel.Visibility);
            Assert.Contains("delivery", searchStatus.Text, StringComparison.OrdinalIgnoreCase);

            EntityNode topicScope = workspace.Search.Roots
                .Single(root => root.Name == "Topics")
                .Children
                .Single(node => node.Name == "events");
            TreeView namespaceTree = (TreeView)window.FindName("NamespaceTree")!;
            namespaceTree.UpdateLayout();
            TreeViewItem topicItem = FindTreeViewItem(namespaceTree, topicScope)
                ?? throw new Xunit.Sdk.XunitException("The projected search topic was not realized in the namespace tree.");
            topicItem.IsSelected = true;
            PumpUntil(dispatcher, () => workspace.Search.Messages.All(row => row.Source.StartsWith("events/", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));
            Assert.True(topicItem.IsSelected, "The selected search scope must remain selected in the rendered tree.");
            Assert.All(workspace.Search.Messages, row => Assert.StartsWith("events/", row.Source, StringComparison.Ordinal));

            continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(dispatcher, () => !workspace.Search.IsBusy && workspace.Search.Messages.Count == 2,
                TimeSpan.FromSeconds(5));
            window.UpdateLayout();
            Assert.Equal("Continue", continueButton.Content);
            Assert.Equal(2, workspace.Search.Messages.Count);
            Assert.All(workspace.Search.Messages, row => Assert.StartsWith("events/", row.Source, StringComparison.Ordinal));
            Assert.Contains("paused", searchStatus.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Visibility.Visible, ((DataGridColumn)messageGrid.Columns.Single(column => column.Header?.ToString() == "Location / State")).Visibility);
            AssertSourceBadges(messageGrid);

            messages.BlockNextPeek();
            continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(dispatcher, () => messages.PeekStarted.Task.IsCompleted && workspace.Search.IsBusy && stopButton.IsEnabled,
                TimeSpan.FromSeconds(5));
            stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(dispatcher, () => !workspace.Search.IsBusy && workspace.Search.Status.StartsWith("Search stopped", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            Assert.Equal(2, workspace.Search.Messages.Count);
            Assert.Contains("stopped", searchStatus.Text, StringComparison.OrdinalIgnoreCase);

            foreach ((double width, double height, string name) in new[]
            {
                (1500, 1000, "search-desktop"),
                (1100, 800, "search-compact"),
                (980, 640, "search-minimum")
            })
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                AssertVisibleBounds(window, searchStatusPanel, searchStatus, messageGrid, continueButton);
                AssertSourceBadges(messageGrid);
                CaptureIfEnabled(window, name);
            }

            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(dispatcher, () => !workspace.Search.IsActive && workspace.Surface.FocusedMessage is not null,
                TimeSpan.FromSeconds(5));
            window.UpdateLayout();
            Assert.Same(browseFocus.Key, workspace.Surface.FocusedMessage!.Key);
            Assert.True(workspace.Inspector.HasDrafts, "Clearing search must retain the browse draft.");
            Assert.Equal("{\"draft\":true}", workspace.Inspector.Document.Text);
            Assert.Equal(string.Empty, searchBox.Text);
            Assert.Equal(Visibility.Collapsed, clearButton.Visibility);
            Assert.Equal(Visibility.Collapsed, searchStatusPanel.Visibility);
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

            messages.ReleasePendingPeek();
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static InvestigationWorkspace CreateWorkspace(
        FakeMessages messages,
        int searchDeliveryBudget = 2,
        EntityDiscoverySnapshot? snapshot = null)
    {
        var preferences = new WorkspacePreferences
        {
            Profiles = [new InvestigationProfile(
                "search-render-proof",
                new ConnectionProfile("Search render proof", "runtime", "admin"))],
            SelectedProfileId = "search-render-proof",
            SearchDeliveryBudget = searchDeliveryBudget,
            SearchTimeBudgetSeconds = 30,
            WindowWidth = 1500,
            WindowHeight = 1000
        };
        var workflow = new BrokerConnectionWorkflow(
            () => new FakeFactory(),
            _ => new FakeBrowser(snapshot ?? CreateSnapshot()),
            _ => messages);
        return new InvestigationWorkspace(new FakeStore(preferences), workflow);
    }

    private static void ShowAndConnect(
        Dispatcher dispatcher,
        InvestigationWorkspace workspace,
        InvestigationWindow window)
    {
        window.Show();
        PumpUntil(dispatcher, () => window.IsVisible && window.ActualWidth > 0 && window.ActualHeight > 0,
            TimeSpan.FromSeconds(5));

        Task connect = workspace.ConnectAsync();
        PumpUntil(dispatcher, () => connect.IsCompleted, TimeSpan.FromSeconds(5));
        connect.GetAwaiter().GetResult();
    }

    private static void RaisePreviewKey(UIElement element, Key key)
    {
        var keyEvent = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(element),
            0,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        element.RaiseEvent(keyEvent);
    }

    private static void ClearSearch(Button clearButton) =>
        clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void CloseWindow(Dispatcher dispatcher, Window window)
    {
        if (!window.IsVisible) return;
        window.Close();
        PumpUntil(dispatcher, () => !window.IsVisible, TimeSpan.FromSeconds(5));
        if (window.IsVisible) window.Hide();
    }

    private static EntityDiscoverySnapshot CreateSnapshot()
    {
        ServiceBusEntityNode queue = Entity(EntityKind.Queue, "orders", null);
        ServiceBusEntityNode topic = Entity(EntityKind.Topic, "events", null);
        ServiceBusEntityNode subscription = Entity(EntityKind.Subscription, "billing", "events");
        return new(
            new[] { queue, topic, subscription }.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);
    }

    private static ServiceBusEntityNode Entity(EntityKind kind, string name, string? topicName) =>
        new(
            kind,
            name,
            topicName,
            new EntityRuntimeCounts(1, 1, 0, 2),
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

    private static TreeViewItem? FindTreeViewItem(DependencyObject root, object dataContext)
    {
        if (root is TreeViewItem item && ReferenceEquals(item.DataContext, dataContext)) return item;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindTreeViewItem(VisualTreeHelper.GetChild(root, index), dataContext) is { } match) return match;
        }

        return null;
    }

    private static void AssertSourceBadges(DataGrid grid)
    {
        bool active = false;
        bool deadLetter = false;
        for (int index = 0; index < grid.Items.Count; index++)
        {
            grid.ScrollIntoView(grid.Items[index]);
            grid.UpdateLayout();
            DataGridRow row = grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow
                ?? throw new Xunit.Sdk.XunitException($"Search message row {index} was not realized.");
            Border badge = FindBadge(row)
                ?? throw new Xunit.Sdk.XunitException($"Search source/state badge for row {index} was not rendered.");
            TextBlock text = FindTextBlock(badge)
                ?? throw new Xunit.Sdk.XunitException($"Search source/state badge text for row {index} was not rendered.");
            active |= text.Text == "Active";
            deadLetter |= text.Text == "DLQ";
        }

        Assert.True(active, "The search render must show an Active source/state badge.");
        Assert.True(deadLetter, "The search render must show a DLQ source/state badge.");
    }

    private static Border? FindBadge(DependencyObject root)
    {
        if (root is Border border && FindTextBlock(border) is { Text: "Active" or "DLQ" }) return border;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindBadge(VisualTreeHelper.GetChild(root, index)) is { } badge) return badge;
        }

        return null;
    }

    private static TextBlock? FindTextBlock(DependencyObject root)
    {
        if (root is TextBlock text && text.Text is "Active" or "DLQ") return text;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindTextBlock(VisualTreeHelper.GetChild(root, index)) is { } childText) return childText;
        }

        return null;
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

    private static void CaptureIfEnabled(Window window, string name)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_INVESTIGATION_UI"), "true", StringComparison.OrdinalIgnoreCase)) return;

        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "artifacts", "investigation-ui");
        Directory.CreateDirectory(directory);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(output);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The search render proof did not reach its expected state.");
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
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> _messages = [];
        private TaskCompletionSource<bool>? _peekStarted;
        private TaskCompletionSource<bool>? _releasePeek;
        private bool _blockNextPeek;

        public FakeMessages()
        {
            EntityAddress queue = new(EntityKind.Queue, "orders");
            EntityAddress subscription = new(EntityKind.Subscription, "billing", "events");
            Set(queue, MessageBucket.Active, Message("active-queue", 1, "Active queue"));
            Set(queue, MessageBucket.DeadLetter, Message("dlq-queue", 2, "DLQ queue"));
            Set(subscription, MessageBucket.Active, Message("active-subscription", 1, "Active subscription"));
            Set(subscription, MessageBucket.DeadLetter, Message("dlq-subscription", 2, "DLQ subscription"));
        }

        public TaskCompletionSource<bool> PeekStarted => _peekStarted ??= new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BlockNextPeek()
        {
            _blockNextPeek = true;
            _peekStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releasePeek = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleasePendingPeek() => _releasePeek?.TrySetResult(true);

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            if (_blockNextPeek)
            {
                _blockNextPeek = false;
                _peekStarted!.TrySetResult(true);
                await _releasePeek!.Task.WaitAsync(cancellationToken);
            }

            IReadOnlyList<ExplorerMessage> values = _messages.TryGetValue((address, bucket), out IReadOnlyList<ExplorerMessage>? found)
                ? found
                : [];
            return values
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToArray();
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

        private void Set(EntityAddress address, MessageBucket bucket, params ExplorerMessage[] values) => _messages[(address, bucket)] = values;

        private static ExplorerMessage Message(string id, long sequence, string subject) => new(
            id,
            sequence,
            $"{{\"messageId\":\"{id}\"}}",
            id,
            id.Length,
            DateTimeOffset.UtcNow,
            null,
            0,
            "application/json",
            $"correlation-{id}",
            null,
            subject,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>())
        {
            RawBody = BinaryData.FromString($"{{\"messageId\":\"{id}\"}}")
        };
    }
}
