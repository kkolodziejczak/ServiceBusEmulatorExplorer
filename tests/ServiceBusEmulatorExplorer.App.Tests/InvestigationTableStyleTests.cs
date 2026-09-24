using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class InvestigationTableStyleTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Workbench_property_and_variable_tables_center_content_and_use_flat_state_colors()
        => RunSta(() =>
        {
            var view = new MessageLibraryPrototypeView();
            var host = Show(view, 1180, 760);
            Click(view, "EditorPropertiesTab");
            host.UpdateLayout();

            AssertTable((DataGrid)view.FindName("ApplicationPropertiesGrid")!, expectedRows: 3);

            var properties = (DataGrid)view.FindName("ApplicationPropertiesGrid")!;
            var typeDisplay = Descendants<ComboBox>(Row(properties, 2)).Single();
            Assert.False(typeDisplay.IsHitTestVisible);
            Assert.Equal(HorizontalAlignment.Center, typeDisplay.HorizontalContentAlignment);
            Assert.Equal("dateTimeUtc", typeDisplay.SelectedItem);
            properties.CurrentCell = new DataGridCellInfo(properties.Items[2], properties.Columns[1]);
            Assert.True(properties.BeginEdit());
            properties.UpdateLayout();
            var typeEditor = Descendants<ComboBox>(Row(properties, 2)).Single();
            Assert.True(typeEditor.IsHitTestVisible);
            typeEditor.SelectedItem = "string";
            Assert.True(properties.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.True(properties.CommitEdit(DataGridEditingUnit.Row, true));
            properties.UpdateLayout();
            Assert.Equal("string", Descendants<ComboBox>(Row(properties, 2)).Single().SelectedItem);
            DataGridRow row = Row(properties, 0);
            AssertBrush(row.Background, "#FFFFFF");
            row.IsSelected = true;
            host.UpdateLayout();
            AssertBrush(row.Background, "#DBEDFF");
            Click(view, "EditorVariablesTab");
            host.UpdateLayout();
            AssertTable((DataGrid)view.FindName("VariablesGrid")!, expectedRows: 4);
            host.Close();
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Dialog_validation_and_results_tables_center_headers_and_values()
        => RunSta(() =>
        {
            var validation = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Validation, "Test", "orders", 2);
            var validationHost = Show(validation, 760, 620);
            validation.SetValidationRows([(1, "C1001", "Ready"), (2, "C1002", "Invalid amount")]);
            validation.UpdateLayout();
            AssertTable((DataGrid)validation.FindName("ValidationGrid")!, expectedRows: 2);
            validation.Close();

            var results = new MessageLibraryPrototypeDialog(PrototypeDialogMode.Results, "Test", "orders", 2);
            var resultsHost = Show(results, 900, 700);
            results.RestoreRun(new PrototypeRunSnapshot(
                "Test", "orders", false,
                [(1, "message-1", "Sent", "Acknowledged"), (2, "message-2", "Sent", "Acknowledged")],
                []));
            results.UpdateLayout();
            AssertTable((DataGrid)results.FindName("ResultsGrid")!, expectedRows: 2);
            results.Close();
        });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Investigation_message_table_centers_headers_and_content()
        => RunSta(() =>
        {
            var profile = new InvestigationProfile("table-proof",
                new ConnectionProfile("Table proof", "runtime", "admin"));
            var preferences = new WorkspacePreferences
            {
                Profiles = [profile],
                SelectedProfileId = profile.Id,
                WindowWidth = 1200,
                WindowHeight = 800
            };
            var workspace = new InvestigationWorkspace(
                new TablePreferencesStore(preferences),
                new BrokerConnectionWorkflow(() => null!, _ => null!, _ => null!));
            var window = new InvestigationWindow(workspace)
            {
                Width = 1200,
                Height = 800,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            Drain(window.Dispatcher);

            var source = new EntityAddress(EntityKind.Queue, "orders");
            var message = new ExplorerMessage(
                "message-1", 1, "{}", "{}", 2, DateTimeOffset.UtcNow, null, 0,
                "application/json", "correlation-1", null, "OrderCreated", new Dictionary<string, object?>(),
                new Dictionary<string, object?>());
            var row = new MessageRow(new MessageDelivery(
                new DeliveryIdentity(1, source, MessageBucket.Active, 1), message), TimestampDisplay.Utc);
            workspace.Browse.Messages.Add(row);
            workspace.Browse.FocusedMessage = row;
            Drain(window.Dispatcher);

            var grid = (DataGrid)window.FindName("MessageGrid")!;
            AssertTable(grid, expectedRows: 1);
            Assert.NotNull(FindText(grid, "OrderCreated"));
            window.Close();
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });

    private static void AssertTable(DataGrid grid, int expectedRows)
    {
        Assert.Equal(expectedRows, grid.Items.Count);
        Assert.Equal(DataGridHeadersVisibility.Column, grid.HeadersVisibility);
        grid.UpdateLayout();
        var headers = Descendants<DataGridColumnHeader>(grid).Where(header => header.IsVisible).ToArray();
        Assert.NotEmpty(headers);
        Assert.All(headers, header =>
        {
            Assert.Equal(HorizontalAlignment.Center, header.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, header.VerticalContentAlignment);
        });

        DataGridRow first = Row(grid, 0);
        var cells = Descendants<DataGridCell>(first).Where(cell => cell.IsVisible).ToArray();
        Assert.NotEmpty(cells);
        Assert.All(cells, cell =>
        {
            Assert.Equal(HorizontalAlignment.Center, cell.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, cell.VerticalContentAlignment);
            if (cell.Column is DataGridTextColumn)
            {
                var text = Assert.IsType<TextBlock>(cell.Content);
                Assert.Equal(TextAlignment.Center, text.TextAlignment);
                Rect contentBounds = text.TransformToAncestor(cell).TransformBounds(new Rect(text.RenderSize));
                Assert.InRange(Math.Abs(contentBounds.Top + contentBounds.Height / 2 - cell.ActualHeight / 2), 0, 2);
            }
        });
    }

    private static DataGridRow Row(DataGrid grid, int index)
    {
        grid.ScrollIntoView(grid.Items[index]);
        grid.UpdateLayout();
        return grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow
            ?? throw new Xunit.Sdk.XunitException($"The row at index {index} was not realized.");
    }

    private static TextBlock? FindText(DependencyObject root, string text) =>
        Descendants<TextBlock>(root).FirstOrDefault(block => block.Text == text);

    private static void AssertBrush(Brush brush, string expected)
    {
        var solid = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(expected, $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}");
    }

    private static void Click(FrameworkElement root, string name) =>
        ((Button)root.FindName(name)!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Window Show(FrameworkElement content, double width, double height)
    {
        var window = content as Window ?? new Window
        {
            Content = content,
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        if (content is Window dialog)
        {
            dialog.Width = width;
            dialog.Height = height;
            dialog.ShowInTaskbar = false;
            dialog.WindowStyle = WindowStyle.None;
        }
        window.Show();
        Drain(window.Dispatcher);
        return window;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "The rendered table proof exceeded its time bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Drain(Dispatcher dispatcher) =>
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class TablePreferencesStore(WorkspacePreferences initial) : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(initial));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
