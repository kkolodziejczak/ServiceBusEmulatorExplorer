using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
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
public sealed class MessageColumnSelectorTests
{
    [Fact]
    public void Catalog_normalizes_ids_and_keeps_absent_application_keys_discoverable()
    {
        Assert.Equal(MessageColumnCatalog.Defaults.ToArray(), MessageColumnCatalog.Normalize(null).ToArray());
        Assert.Equal(MessageColumnCatalog.Defaults.ToArray(),
            MessageColumnCatalog.Normalize(["unknown", "application:"]).ToArray());
        Assert.Equal(
            new[] { MessageColumnCatalog.Correlation, "system:DeliveryCount", "application:gone" },
            MessageColumnCatalog.Normalize([
                MessageColumnCatalog.Correlation, "unknown", "system:DeliveryCount",
                "application:gone", "application:gone", "application:"]));

        MessageRow first = CreateRow("one", 3, new Dictionary<string, object?>
        {
            ["tenantId"] = "tenant-A",
            ["nullable"] = null,
            ["empty"] = string.Empty,
            ["DeliveryCount"] = "application-shadow"
        });
        MessageRow second = CreateRow("two", 12, new Dictionary<string, object?>
        {
            ["tenantId"] = 42,
            ["otherKey"] = true
        });
        IReadOnlyList<MessageColumnDefinition> discovered = MessageColumnCatalog.Discover(
            [first, second], ["application:gone"]);
        Assert.Contains(discovered, column => column.Id == "application:gone" && column.Label == "gone");
        Assert.Contains(discovered, column => column.Id == "application:tenantId");
        Assert.Contains(discovered, column => column.Id == "application:otherKey");

        ExplorerMessage message = first.Delivery.Message;
        Assert.Equal(3, MessageColumnCatalog.Value(message, "system:DeliveryCount"));
        Assert.Equal("application-shadow", MessageColumnCatalog.Value(message, "application:DeliveryCount"));
        Assert.Null(MessageColumnCatalog.Value(message, "application:missing"));
        Assert.Null(MessageColumnCatalog.Value(message, "application:nullable"));
        Assert.Equal(string.Empty, MessageColumnCatalog.Value(message, "application:empty"));
    }

    [Fact]
    public void Catalog_formats_values_invariantly_and_compares_typed_values()
    {
        var preferences = new WorkspacePreferences { DateFormat = DateDisplayFormat.Iso };
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1234.5", MessageColumnCatalog.Format(1234.5m, preferences));
            Assert.Equal("\u2014", MessageColumnCatalog.Format(null, preferences));
            Assert.Equal(string.Empty, MessageColumnCatalog.Format(string.Empty, preferences));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.True(MessageColumnCatalog.Compare(null, 0) < 0);
        Assert.True(MessageColumnCatalog.Compare(2, 10) < 0);
        Assert.Equal(0, MessageColumnCatalog.Compare(7, 7m));
        Assert.True(MessageColumnCatalog.Compare(DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1)) < 0);
    }

    [Theory]
    [InlineData(1500, 1000)]
    [InlineData(1100, 800)]
    [InlineData(980, 640)]
    [Trait("TestCategory", "UiRender")]
    public void Popup_filters_and_applies_columns_immediately_at_supported_widths(int width, int height) =>
        RunSta(() => ExercisePopup(width, height));

    private static void ExercisePopup(int width, int height)
    {
        var profile = new InvestigationProfile("columns-proof",
            new ConnectionProfile("Columns proof", "runtime", "admin"));
        var initial = new WorkspacePreferences
        {
            Profiles = [profile],
            SelectedProfileId = profile.Id,
            WindowWidth = width,
            WindowHeight = height,
            MessageColumns = MessageColumnCatalog.Defaults.Append("application:retired").ToArray()
        };
        var store = new TablePreferencesStore(initial);
        var workspace = new InvestigationWorkspace(
            store,
            new BrokerConnectionWorkflow(() => null!, _ => null!, _ => null!));
        var window = new InvestigationWindow(workspace)
        {
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize
        };

        try
        {
            workspace.InitializeAsync().GetAwaiter().GetResult();
            window.Show();
            Drain(window.Dispatcher);
            var grid = (DataGrid)window.FindName("MessageGrid")!;
            var selectAll = (CheckBox)window.FindName("SelectAllBox")!;
            var columnsButton = (Button)window.FindName("ColumnsButton")!;
            var popup = (Popup)window.FindName("ColumnsPopup")!;
            var search = (TextBox)window.FindName("ColumnsSearchBox")!;
            var choices = (ItemsControl)window.FindName("ColumnsChoices")!;
            var reset = (Button)window.FindName("ColumnsResetButton")!;
            var done = (Button)window.FindName("ColumnsDoneButton")!;

            Assert.Equal(MessageColumnCatalog.Defaults.Append("application:retired"),
                workspace.Preferences.MessageColumns!);
            MessageRow first = CreateRow("columns-one", 7, new Dictionary<string, object?>
            {
                ["tenantId"] = "tenant-A",
                ["createdAt"] = DateTimeOffset.UnixEpoch.AddDays(1),
                ["DeliveryCount"] = "application-shadow"
            });
            MessageRow second = CreateRow("columns-two", 12, new Dictionary<string, object?>
            {
                ["tenantId"] = 42,
                ["otherKey"] = true
            });
            workspace.Browse.Messages.Add(first);
            workspace.Browse.Messages.Add(second);
            workspace.Browse.FocusedMessage = first;
            Drain(window.Dispatcher);
            window.UpdateLayout();

            Assert.True(selectAll.IsVisible, "The message-selection checkbox must remain available.");
            DataGridRow renderedFirst = Row(grid, first);
            Assert.Contains(Descendants<CheckBox>(renderedFirst), box => box.IsVisible);
            AssertColumn(grid, MessageColumnCatalog.Event, Visibility.Visible);
            AssertColumn(grid, MessageColumnCatalog.Correlation, Visibility.Visible);
            AssertColumn(grid, MessageColumnCatalog.Enqueued, Visibility.Visible);
            var hoverRow = Row(grid, second);
            foreach (string resource in new[] { "NeutralHoverBrush", "RaisedBrush" })
            {
                hoverRow.Background = (Brush)window.FindResource(resource);
                window.UpdateLayout();
                Assert.All(Descendants<DataGridCell>(hoverRow).Where(cell => cell.IsVisible),
                    cell => Assert.Equal(hoverRow.Background, cell.Background));
            }
            hoverRow.ClearValue(Control.BackgroundProperty);

            OpenPopup(window, columnsButton, popup, search, choices);
            Assert.InRange(Math.Abs(window.ActualWidth - width), 0, 2);
            ICollectionView view = Assert.IsAssignableFrom<ICollectionView>(choices.ItemsSource);
            Assert.NotEmpty(view.Groups!);
            Assert.Contains(view.Cast<MessageColumnOption>(), option => option.Id == "system:DeliveryCount");
            Assert.Contains(view.Cast<MessageColumnOption>(), option => option.Id == "application:tenantId");
            MessageColumnOption retired = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == "application:retired");
            Assert.True(retired.IsSelected, "A saved application key stays selected when no loaded message has it.");
            AssertColumn(grid, retired.Id, Visibility.Visible);
            Assert.Equal("\u2014", ColumnText(grid, Row(grid, first), retired.Id));
            Assert.Equal(new[] { "DeliveryCount", "createdAt", "otherKey", "tenantId" }, view.Cast<MessageColumnOption>()
                .Where(option => option.Section == "Application properties")
                .Select(option => option.Label).ToArray());
            CapturePopupIfEnabled(popup, width, height);

            search.Text = "tenantId";
            Drain(window.Dispatcher);
            Assert.Single(view.Cast<MessageColumnOption>(), option => option.Id == "application:tenantId");
            search.Text = "no-column-matches-this";
            Drain(window.Dispatcher);
            Assert.Empty(view.Cast<MessageColumnOption>());
            search.Clear();
            Drain(window.Dispatcher);

            MessageColumnOption deliveryCount = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == "system:DeliveryCount");
            Assert.False(deliveryCount.IsSelected);
            ToggleOption(choices, deliveryCount.Id, window.Dispatcher);
            Assert.True(deliveryCount.IsSelected);
            AssertColumn(grid, deliveryCount.Id, Visibility.Visible);
            Assert.Equal(deliveryCount.Label, grid.Columns.Single(column => column.SortMemberPath == deliveryCount.Id).Header);
            Assert.Equal("7", ColumnText(grid, Row(grid, first), deliveryCount.Id));
            Assert.Contains(workspace.Preferences.MessageColumns!, id => id == deliveryCount.Id);
            AssertColumnOrdering(workspace, window, grid, choices, store, deliveryCount.Id);

            // Exercise the actual DataGrid Sorting routed event and ensure integer values sort numerically.
            DataGridColumn deliveryColumn = grid.Columns.Single(column => column.SortMemberPath == deliveryCount.Id);
            RaiseSorting(grid, deliveryColumn);
            Assert.Same(first, grid.Items[0]);
            RaiseSorting(grid, deliveryColumn);
            Assert.Same(second, grid.Items[0]);

            MessageColumnOption tenantId = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == "application:tenantId");
            ToggleOption(choices, tenantId.Id, window.Dispatcher);
            Assert.True(tenantId.IsSelected);
            AssertColumn(grid, tenantId.Id, Visibility.Visible);
            Assert.Equal("tenant-A", ColumnText(grid, Row(grid, first), tenantId.Id));
            Assert.Equal(workspace.Preferences.MessageColumns!.ToArray(), store.Saved.Last().MessageColumns!.ToArray());
            Assert.Equal("application-shadow", MessageColumnCatalog.Format(
                MessageColumnCatalog.Value(first.Delivery.Message, "application:DeliveryCount"), workspace.Preferences));
            MessageColumnOption appDeliveryCount = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == "application:DeliveryCount");
            ToggleOption(choices, appDeliveryCount.Id, window.Dispatcher);
            Assert.Equal("application-shadow", ColumnText(grid, Row(grid, first), appDeliveryCount.Id));
            MessageColumnOption createdAt = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == "application:createdAt");
            ToggleOption(choices, createdAt.Id, window.Dispatcher);
            Assert.Equal(MessageColumnCatalog.Format(DateTimeOffset.UnixEpoch.AddDays(1), workspace.Preferences),
                ColumnText(grid, Row(grid, first), createdAt.Id));

            // Observable delivery replacement refreshes both system and application cells; date preferences
            // reformat the same typed timestamp in-place.
            var refreshedMessage = first.Delivery.Message with
            {
                DeliveryCount = 19,
                EnqueuedTime = DateTimeOffset.UnixEpoch.AddDays(2).AddHours(3),
                ApplicationProperties = new Dictionary<string, object?>
                {
                    ["tenantId"] = "tenant-updated",
                    ["createdAt"] = DateTimeOffset.UnixEpoch.AddDays(2).AddHours(3),
                    ["DeliveryCount"] = "application-updated"
                }
            };
            first.UpdateDelivery(new MessageDelivery(first.Delivery.Identity, refreshedMessage));
            Drain(window.Dispatcher);
            Assert.Same(first, grid.Items[0]);
            Assert.Equal("19", ColumnText(grid, Row(grid, first), deliveryCount.Id));
            Assert.Equal("tenant-updated", ColumnText(grid, Row(grid, first), tenantId.Id));
            Assert.Equal("application-updated", ColumnText(grid, Row(grid, first), appDeliveryCount.Id));
            Assert.Equal(MessageColumnCatalog.Format(refreshedMessage.ApplicationProperties["createdAt"], workspace.Preferences),
                ColumnText(grid, Row(grid, first), createdAt.Id));
            string windowsDateText = ColumnText(grid, Row(grid, first), createdAt.Id);
            workspace.ApplyPreferencesAsync(workspace.Preferences with { DateFormat = DateDisplayFormat.Iso })
                .GetAwaiter().GetResult();
            Drain(window.Dispatcher);
            string isoDateText = ColumnText(grid, Row(grid, first), createdAt.Id);
            Assert.NotEqual(windowsDateText, isoDateText);
            Assert.Equal(MessageColumnCatalog.Format(refreshedMessage.ApplicationProperties["createdAt"], workspace.Preferences), isoDateText);
            DataGridColumn enqueuedColumn = grid.Columns.Single(column => column.SortMemberPath == MessageColumnCatalog.Enqueued);
            grid.ScrollIntoView(first, enqueuedColumn);
            grid.UpdateLayout();
            Assert.Equal(first.EnqueuedDisplay, ColumnText(grid, Row(grid, first), MessageColumnCatalog.Enqueued));
            grid.ScrollIntoView(first, grid.Columns.Single(column => column.SortMemberPath == appDeliveryCount.Id));
            grid.UpdateLayout();
            CaptureWindowIfEnabled(window, width, height, "selected-properties");

            MessageColumnOption correlation = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == MessageColumnCatalog.Correlation);
            ToggleOption(choices, correlation.Id, window.Dispatcher);
            Assert.False(correlation.IsSelected);
            AssertColumn(grid, correlation.Id, Visibility.Collapsed);
            Assert.Contains(Descendants<CheckBox>(Row(grid, first)), box => box.IsVisible);

            ToggleOption(choices, tenantId.Id, window.Dispatcher);
            ToggleOption(choices, deliveryCount.Id, window.Dispatcher);
            ToggleOption(choices, appDeliveryCount.Id, window.Dispatcher);
            ToggleOption(choices, createdAt.Id, window.Dispatcher);
            ToggleOption(choices, retired.Id, window.Dispatcher);
            AssertColumn(grid, tenantId.Id, Visibility.Collapsed);
            AssertColumn(grid, deliveryCount.Id, Visibility.Collapsed);
            AssertColumn(grid, appDeliveryCount.Id, Visibility.Collapsed);
            AssertColumn(grid, createdAt.Id, Visibility.Collapsed);
            AssertColumn(grid, retired.Id, Visibility.Collapsed);

            MessageColumnOption eventColumn = Assert.Single(view.Cast<MessageColumnOption>(),
                option => option.Id == MessageColumnCatalog.Event);
            ToggleOption(choices, eventColumn.Id, window.Dispatcher);
            Assert.False(eventColumn.IsSelected);
            AssertColumn(grid, eventColumn.Id, Visibility.Collapsed);
            MessageColumnOption lastSelected = Assert.Single(view.Cast<MessageColumnOption>(), option => option.IsSelected);
            Assert.Equal(MessageColumnCatalog.Enqueued, lastSelected.Id);
            Assert.False(lastSelected.CanToggle);
            Assert.False(FindChoice(choices, lastSelected.Id).IsEnabled);

            Click(reset);
            Drain(window.Dispatcher);
            Assert.Equal(MessageColumnCatalog.Defaults.ToArray(), workspace.Preferences.MessageColumns!.ToArray());
            Assert.Equal(MessageColumnCatalog.Defaults, workspace.Preferences.MessageColumnOrder);
            Assert.All(MessageColumnCatalog.Defaults, id => AssertColumn(grid, id, Visibility.Visible));
            AssertColumn(grid, deliveryCount.Id, Visibility.Collapsed);
            AssertColumn(grid, tenantId.Id, Visibility.Collapsed);

            // A persistence error restores the last saved selection and reports the failure in workspace status.
            var dateColumn = grid.Columns.Single(column => column.SortMemberPath == MessageColumnCatalog.Enqueued);
            dateColumn.DisplayIndex = 3;
            typeof(DataGrid).GetMethod("OnColumnReordered", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grid, [new DataGridColumnEventArgs(dateColumn)]);
            Drain(window.Dispatcher);
            Assert.False(dateColumn.Width.IsStar);
            Assert.True(((DataGridColumn)window.FindName("CorrelationColumn")!).Width.IsStar);
            CaptureWindowIfEnabled(window, width, height, "date-middle");
            Click(reset);
            Drain(window.Dispatcher);

            store.ThrowOnSave = true;
            ToggleOption(choices, deliveryCount.Id, window.Dispatcher);
            Assert.False(deliveryCount.IsSelected);
            AssertColumn(grid, deliveryCount.Id, Visibility.Collapsed);
            var operationStatus = (TextBlock)window.FindName("LastOperation")!;
            Assert.Contains("Restored the last saved selection", operationStatus.Text,
                StringComparison.OrdinalIgnoreCase);
            store.ThrowOnSave = false;

            search.Focus();
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(search) ?? throw new Xunit.Sdk.XunitException("Search box has no presentation source."),
                Environment.TickCount, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            search.RaiseEvent(escape);
            Drain(window.Dispatcher);
            Assert.False(popup.IsOpen);
            Assert.True(columnsButton.IsKeyboardFocused);
            AssertScopeAndProfileTransitions(workspace, window, grid, columnsButton);

            OpenPopup(window, columnsButton, popup, search, choices);
            Click(done);
            Drain(window.Dispatcher);
            Assert.False(popup.IsOpen);
            Assert.True(columnsButton.IsKeyboardFocused);

        }
        finally
        {
            window.Close();
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void OpenPopup(Window window, Button button, Popup popup, TextBox search, ItemsControl choices)
    {
        if (!popup.IsOpen)
        {
            button.Focus();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }
        Drain(window.Dispatcher);
        window.UpdateLayout();
        choices.UpdateLayout();
        Assert.True(popup.IsOpen);
        Assert.True(search.IsKeyboardFocused, "Opening the selector should focus its search field.");
    }

    private static void AssertColumnOrdering(InvestigationWorkspace workspace, Window window, DataGrid grid,
        ItemsControl choices, TablePreferencesStore store, string added)
    {
        var state = (DataGridColumn)window.FindName("StateColumn")!;
        Assert.Equal(2, grid.FrozenColumnCount);
        Assert.Equal(1, state.DisplayIndex);
        Assert.False(state.CanUserReorder);
        Assert.False(grid.Columns[0].CanUserReorder);
        Assert.Equal(Visibility.Visible, state.Visibility);
        string[] VisibleOrder() => grid.Columns.Where(column => column.Visibility == Visibility.Visible &&
                !string.IsNullOrEmpty(column.SortMemberPath)).OrderBy(column => column.DisplayIndex)
            .Select(column => column.SortMemberPath).ToArray();
        var before = VisibleOrder();
        Assert.Equal(added, before[^1]);
        Button Move(string id, string direction) => Descendants<Button>(choices).Single(button =>
            button.DataContext is MessageColumnOption option && option.Id == id && button.Tag as string == direction);
        Click(Move(added, "Up"));
        Drain(window.Dispatcher);
        Assert.Equal(added, VisibleOrder()[^2]);
        Assert.Equal(VisibleOrder(), workspace.Preferences.MessageColumnOrder!.Where(workspace.Preferences.MessageColumns!.Contains));
        ToggleOption(choices, added, window.Dispatcher);
        ToggleOption(choices, added, window.Dispatcher);
        Assert.Equal(added, VisibleOrder()[^2]);

        // Exercise the DataGrid completion event used by header dragging, without physical mouse input.
        var moved = grid.Columns.Single(column => column.SortMemberPath == added);
        moved.DisplayIndex = 2;
        typeof(DataGrid).GetMethod("OnColumnReordered", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridColumnEventArgs(moved)]);
        Drain(window.Dispatcher);
        Assert.Equal(added, VisibleOrder()[0]);
        Assert.Equal(added, workspace.Preferences.MessageColumnOrder![0]);
        Assert.False(Move(added, "Up").IsEnabled);
        var displayed = grid.Columns.Where(column => column.Visibility == Visibility.Visible &&
            column != state && column != grid.Columns[0]).OrderBy(column => column.DisplayIndex).ToArray();
        Assert.True(displayed[^1].Width.IsStar, "The last visible column must fill the available width, regardless of property.");
        Assert.All(displayed[..^1], column => Assert.False(column.Width.IsStar, $"Middle column {column.Header} must not retain fill width."));
        var saved = VisibleOrder();
        store.ThrowOnSave = true;
        Click(Move(added, "Down"));
        Drain(window.Dispatcher);
        Assert.Equal(saved, VisibleOrder());
        Assert.Equal(saved, workspace.Preferences.MessageColumnOrder!.Where(workspace.Preferences.MessageColumns!.Contains));
        store.ThrowOnSave = false;
    }

    private static void AssertScopeAndProfileTransitions(InvestigationWorkspace workspace, Window window, DataGrid grid, Button button)
    {
        workspace.UpdateMessageColumnsAsync(["system:DeliveryCount"]).GetAwaiter().GetResult();
        EntityObservation Observation(EntityKind kind, string name, string? topic = null) => new(
            new ServiceBusEntityNode(kind, name, topic, new(1, 1, 0, 2), new(name, "Active", null, null, null, null, null, null, null)),
            new(new(1, CountAvailability.Known), new(1, CountAvailability.Known), new(0, CountAvailability.Known)));
        var queue = new EntityNode("orders", nameof(EntityKind.Queue), Observation(EntityKind.Queue, "orders"));
        var topic = new EntityNode("events", nameof(EntityKind.Topic), Observation(EntityKind.Topic, "events"));
        var subscription = new EntityNode("billing", nameof(EntityKind.Subscription), Observation(EntityKind.Subscription, "billing", "events"));
        topic.Children.Add(subscription);
        var snapshot = new EntityDiscoverySnapshot([queue.Observation!, topic.Observation!, subscription.Observation!], DateTimeOffset.UtcNow, true, []);
        var session = new BrokerSession(null!, new ScopeBrowser(snapshot), new ScopeMessages(), snapshot, null);
        workspace.Browse.SetSession(session, 1);
        workspace.Search.SetSession(session, 1);
        foreach (var (entity, deadLetter) in new[] { (queue, false), (queue, true), (subscription, false), (subscription, true) })
        {
            workspace.Browse.SelectAsync(entity, deadLetter).GetAwaiter().GetResult();
            Drain(window.Dispatcher);
            AssertColumn(grid, "system:DeliveryCount", Visibility.Visible);
            AssertColumn(grid, MessageColumnCatalog.Event, Visibility.Collapsed);
            Assert.Equal(Visibility.Visible, button.Visibility);
            var state = (DataGridColumn)window.FindName("StateColumn")!;
            Assert.Equal(Visibility.Visible, state.Visibility);
            Assert.Equal(1, state.DisplayIndex);
            Assert.True(grid.CanUserReorderColumns);
            foreach (var row in grid.Items.OfType<MessageRow>())
                Assert.Contains(Descendants<TextBlock>(state.GetCellContent(Row(grid, row))), text => text.Text == row.StateLabel);
        }
        workspace.Browse.SelectAsync(topic, false).GetAwaiter().GetResult();
        Drain(window.Dispatcher);
        AssertColumn(grid, "system:DeliveryCount", Visibility.Collapsed);
        AssertColumn(grid, MessageColumnCatalog.Event, Visibility.Visible);
        Assert.Equal(Visibility.Visible, ((DataGridColumn)window.FindName("SourceColumn")!).Visibility);
        Assert.Equal(Visibility.Collapsed, button.Visibility);
        Assert.False(grid.CanUserReorderColumns);
        Assert.Equal("Location", ((DataGridColumn)window.FindName("SourceColumn")!).Header);
        workspace.Browse.SelectAsync(subscription, false).GetAwaiter().GetResult();
        workspace.Search.StartAsync("*").GetAwaiter().GetResult();
        Drain(window.Dispatcher);
        Assert.True(workspace.Surface.IsSearch);
        AssertColumn(grid, "system:DeliveryCount", Visibility.Collapsed);
        Assert.Equal(Visibility.Collapsed, button.Visibility);
        workspace.Search.Clear();
        Drain(window.Dispatcher);
        AssertColumn(grid, "system:DeliveryCount", Visibility.Visible);
        var other = new InvestigationProfile("other", new ConnectionProfile("Other", "runtime", "admin"));
        workspace.ApplyPreferencesAsync(workspace.Preferences with { Profiles = [.. workspace.Preferences.Profiles, other] }).GetAwaiter().GetResult();
        Assert.True(workspace.SwitchProfileAsync(other).GetAwaiter().GetResult());
        Drain(window.Dispatcher);
        Assert.Equal(new[] { "system:DeliveryCount" }, workspace.Preferences.MessageColumns);
        AssertColumn(grid, "system:DeliveryCount", Visibility.Visible);
    }

    private sealed class ScopeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class ScopeMessages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take, long? fromSequenceNumber, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExplorerMessage>>(fromSequenceNumber is null or <= 1 ? [CreateRow("scope-message", 1, new Dictionary<string, object?>()).Delivery.Message] : []);
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static void ToggleOption(ItemsControl choices, string id, Dispatcher dispatcher)
    {
        CheckBox option = FindChoice(choices, id);
        Assert.True(option.IsEnabled);
        option.IsChecked = !(option.IsChecked ?? false);
        option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, option));
        Drain(dispatcher);
    }

    private static CheckBox FindChoice(ItemsControl choices, string id)
    {
        CheckBox box = Descendants<CheckBox>(choices)
            .Single(candidate => (candidate.DataContext as MessageColumnOption)?.Id == id);
        var option = Assert.IsType<MessageColumnOption>(box.DataContext);
        Assert.Equal(option.Label, box.Content);
        Assert.Equal(option.Label, System.Windows.Automation.AutomationProperties.GetName(box));
        return box;
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static void RaiseSorting(DataGrid grid, DataGridColumn column)
    {
        var args = new DataGridSortingEventArgs(column);
        typeof(DataGrid).GetMethod("OnSorting", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [args]);
    }

    private static void AssertColumn(DataGrid grid, string id, Visibility expected)
    {
        DataGridColumn column = grid.Columns.Single(item => item.SortMemberPath == id);
        Assert.Equal(expected, column.Visibility);
    }

    private static string ColumnText(DataGrid grid, DataGridRow row, string id)
    {
        DataGridColumn column = grid.Columns.Single(item => item.SortMemberPath == id);
        return Assert.IsType<TextBlock>(column.GetCellContent(row)).Text;
    }

    private static DataGridRow Row(DataGrid grid, MessageRow message)
    {
        grid.ScrollIntoView(message);
        grid.UpdateLayout();
        return grid.ItemContainerGenerator.ContainerFromItem(message) as DataGridRow
            ?? throw new Xunit.Sdk.XunitException("The message row was not realized.");
    }

    private static MessageRow CreateRow(string id, int deliveryCount, IReadOnlyDictionary<string, object?> applicationProperties)
    {
        var message = new ExplorerMessage(
            id, deliveryCount, "{}", "{}", 2, DateTimeOffset.UnixEpoch.AddSeconds(deliveryCount), null, deliveryCount,
            "application/json", "correlation-" + id, null, "Subject", applicationProperties,
            new Dictionary<string, object?> { ["DeliveryCount"] = "system-shadow" });
        var source = new EntityAddress(EntityKind.Queue, "orders");
        var delivery = new MessageDelivery(
            new DeliveryIdentity(1, source, MessageBucket.Active, deliveryCount), message);
        return new MessageRow(delivery, TimestampDisplay.Utc);
    }

    private static void CapturePopupIfEnabled(Popup popup, int width, int height)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_MESSAGE_COLUMNS"), "true", StringComparison.OrdinalIgnoreCase)
            || popup.Child is not FrameworkElement child)
            return;

        child.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(child.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(child.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(child);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")) && !File.Exists(Path.Combine(root.FullName, ".git")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Could not locate the repository root for the selector proof image.");

        string outputDirectory = Path.Combine(root.FullName, "artifacts", "columns41-proof");
        Directory.CreateDirectory(outputDirectory);
        using var output = new FileStream(
            Path.Combine(outputDirectory, $"columns41-{width}x{height}-popup.png"),
            FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(output);
    }

    private static void CaptureWindowIfEnabled(Window window, int width, int height, string state)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_MESSAGE_COLUMNS"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")) && !File.Exists(Path.Combine(root.FullName, ".git")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Could not locate the repository root for the selector proof image.");

        string outputDirectory = Path.Combine(root.FullName, "artifacts", "columns41-proof");
        Directory.CreateDirectory(outputDirectory);
        using var output = new FileStream(
            Path.Combine(outputDirectory, $"columns41-{width}x{height}-{state}.png"),
            FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void RunSta(Action assertion)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { assertion(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Message-column WPF proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class TablePreferencesStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public List<WorkspacePreferences> Saved { get; } = [];
        public bool ThrowOnSave { get; set; }

        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            if (ThrowOnSave) throw new IOException("Synthetic preferences write failure.");
            Saved.Add(preferences);
            return Task.CompletedTask;
        }
    }
}
