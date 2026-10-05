using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public void Workbench_property_and_variable_tables_left_align_content_and_use_flat_state_colors()
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
            Assert.Equal(HorizontalAlignment.Left, typeDisplay.HorizontalContentAlignment);
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
    public void Dialog_validation_and_results_tables_left_align_headers_and_values()
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
    public void Investigation_message_table_left_aligns_headers_and_content()
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
            DataGridColumn correlationColumn = (DataGridColumn)window.FindName("CorrelationColumn")!;
            DataGridColumn sourceColumn = (DataGridColumn)window.FindName("SourceColumn")!;
            Assert.Equal(Visibility.Visible, correlationColumn.Visibility);
            Assert.Equal(Visibility.Collapsed, sourceColumn.Visibility);

            // The row's Copy action is hosted inside this cell; focus on it must not add
            // a cell frame or erase the selected-row color when WPF focus cues are hidden.
            DataGridRow correlationRow = Row(grid, 0);
            correlationRow.IsSelected = true;
            grid.CurrentCell = new DataGridCellInfo(correlationRow, correlationColumn);
            DataGridCell correlationCell = Descendants<DataGridCell>(correlationRow)
                .Single(cell => cell.Column == correlationColumn);
            Button rowCopy = Descendants<Button>(correlationCell)
                .Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Copy row correlation ID");
            Assert.True(correlationCell.Focus());
            Assert.True(correlationCell.IsKeyboardFocusWithin);
            Assert.Same(window.FindResource("KeyboardActionFocusVisual"), correlationCell.FocusVisualStyle);

            // WPF normally shows FocusVisualStyle only after keyboard navigation. Force
            // that WPF state in this headless rendered test and inspect its actual adorner.
            Type keyboardNavigation = typeof(System.Windows.Input.KeyboardNavigation);
            PropertyInfo alwaysShowFocusVisual = keyboardNavigation.GetProperty(
                "AlwaysShowFocusVisual", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(keyboardNavigation.FullName, "AlwaysShowFocusVisual");
            MethodInfo showFocusVisual = keyboardNavigation.GetMethod(
                "ShowFocusVisual", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(keyboardNavigation.FullName, "ShowFocusVisual");
            object previousAlwaysShow = alwaysShowFocusVisual.GetValue(null)!;
            try
            {
                alwaysShowFocusVisual.SetValue(null, true);
                showFocusVisual.Invoke(null, null);
                Assert.True(correlationCell.Focus());
                Drain(window.Dispatcher);
                AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(correlationCell)
                    ?? throw new Xunit.Sdk.XunitException("The focused Copy button has no WPF adorner layer.");
                Adorner[] focusAdorners = adornerLayer.GetAdorners(correlationCell) ?? [];
                Border keyboardCue = Assert.Single(
                    focusAdorners.SelectMany(adorner => Descendants<Border>(adorner)),
                    border => border.BorderThickness == new Thickness(0, 0, 0, 2));
                Assert.Equal(window.FindResource("PrimaryBrush"), keyboardCue.BorderBrush);
                CaptureFocusedCorrelationCellIfRequested(window, "correlation-cell-keyboard-focused.png");
            }
            finally
            {
                alwaysShowFocusVisual.SetValue(null, previousAlwaysShow);
                System.Windows.Input.Keyboard.ClearFocus();
            }

            Assert.True(rowCopy.Focus());
            Drain(window.Dispatcher);
            Assert.True(rowCopy.IsKeyboardFocused);
            Assert.True(correlationRow.IsSelected);
            AssertBrush(correlationRow.Background, "#DBEDFF");
            AssertBrush(correlationCell.Background, "#DBEDFF");
            Assert.Same(window.FindResource("KeyboardActionFocusVisual"), rowCopy.FocusVisualStyle);
            CaptureFocusedCorrelationCellIfRequested(window, "correlation-cell-focused-without-keyboard-cue.png");
            Assert.Equal(new Thickness(0), correlationCell.BorderThickness);

            // Force the shared location template into view to prove its one-line queue branch.
            sourceColumn.Visibility = Visibility.Visible;
            correlationColumn.Visibility = Visibility.Collapsed;
            AssertTable(grid, expectedRows: 1, stretchedColumns:
                [(DataGridColumn)window.FindName("EventColumn")!,
                 sourceColumn]);
            Assert.NotNull(FindText(grid, "OrderCreated"));

            Assert.Equal("correlation-1", ((TextBlock)window.FindName("CorrelationValue")!).Text);
            var inspectorCopy = (Button)window.FindName("CopyCorrelationButton")!;
            Assert.True(inspectorCopy.IsVisible);
            Assert.Equal("Copy correlation ID", System.Windows.Automation.AutomationProperties.GetName(inspectorCopy));

            Assert.Equal(EntityKind.Queue, row.SourceKind);
            Assert.Equal(string.Empty, row.SourceTopic);
            Assert.Equal("orders", row.SourceName);
            Assert.False(row.IsSubscription);
            Assert.Equal("Queue: orders", row.SourceDetail);
            DataGridRow renderedRow = Row(grid, 0);
            DataGridCell sourceCell = Descendants<DataGridCell>(renderedRow).Single(cell => cell.Column == sourceColumn);
            FrameworkElement topicLine = Descendants<FrameworkElement>(sourceCell).Single(element => element.Name == "SourceTopicLine");
            FrameworkElement nameLine = Descendants<FrameworkElement>(sourceCell).Single(element => element.Name == "SourceNameLine");
            TextBlock topicText = Descendants<TextBlock>(sourceCell).Single(text => text.Name == "SourceTopicText");
            TextBlock sourceName = Descendants<TextBlock>(sourceCell).Single(text => text.Name == "SourceNameText");
            System.Windows.Shapes.Path queueIcon = Descendants<System.Windows.Shapes.Path>(sourceCell)
                .Single(icon => icon.Name == "SourceNameIcon");
            Assert.Equal(Visibility.Collapsed, topicLine.Visibility);
            Assert.False(topicText.IsVisible);
            Assert.Equal(Visibility.Visible, nameLine.Visibility);
            Assert.Equal("orders", sourceName.Text);
            Assert.Equal(row.SourceDetail, sourceName.ToolTip);
            Assert.Equal(window.FindResource("QueueGeometry"), queueIcon.Data);
            window.Close();
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });

    private static void AssertTable(DataGrid grid, int expectedRows, DataGridColumn[]? stretchedColumns = null)
    {
        Assert.Equal(expectedRows, grid.Items.Cast<object>().Count(item =>
            !ReferenceEquals(item, CollectionView.NewItemPlaceholder)));
        Assert.Equal(DataGridHeadersVisibility.Column, grid.HeadersVisibility);
        grid.UpdateLayout();
        var headers = Descendants<DataGridColumnHeader>(grid).Where(header => header.IsVisible).ToArray();
        Assert.NotEmpty(headers);
        var dataHeaders = headers.Where(header => header.Column?.Header is { } value
            && value is not CheckBox && !string.IsNullOrWhiteSpace(value.ToString())).ToArray();
        Assert.NotEmpty(dataHeaders);
        Assert.All(dataHeaders, header =>
        {
            Assert.Equal(HorizontalAlignment.Left, header.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, header.VerticalContentAlignment);
        });

        DataGridRow first = Row(grid, 0);
        var cells = Descendants<DataGridCell>(first).Where(cell => cell.IsVisible).ToArray();
        Assert.NotEmpty(cells);
        Assert.All(cells, cell =>
        {
            bool reservesTrailingAction = stretchedColumns?.Contains(cell.Column) == true;
            Assert.Equal(reservesTrailingAction ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                cell.HorizontalContentAlignment);
            if (reservesTrailingAction)
            {
                // Fill the cell to reserve the trailing badge/copy action, while keeping text left aligned.
                var labels = Descendants<TextBlock>(cell).Where(text => text.IsVisible).ToArray();
                if (cell.Column.Header?.ToString() == "Location")
                    labels = labels.Where(text => text.Name is "SourceNameText" or "SourceTopicText").ToArray();
                Assert.NotEmpty(labels);
                Assert.All(labels, text => Assert.Equal(TextAlignment.Left, text.TextAlignment));
            }
            Assert.Equal(VerticalAlignment.Center, cell.VerticalContentAlignment);
            if (cell.Column is DataGridTextColumn)
            {
                var text = Assert.IsType<TextBlock>(cell.Content);
                Assert.Equal(TextAlignment.Left, text.TextAlignment);
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

    private static void CaptureFocusedCorrelationCellIfRequested(Window window, string fileName)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_CAPTURE_CELL_FOCUS"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")) && !File.Exists(Path.Combine(root.FullName, ".git")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Could not locate the repository root for the focused-cell proof image.");

        string outputDirectory = Path.Combine(root.FullName, "artifacts", "cell-focus-proof");
        Directory.CreateDirectory(outputDirectory);
        using var output = new FileStream(
            Path.Combine(outputDirectory, fileName),
            FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(output);
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
