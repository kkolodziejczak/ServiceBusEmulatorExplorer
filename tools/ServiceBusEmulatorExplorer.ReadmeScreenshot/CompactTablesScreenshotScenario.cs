using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class CompactTablesScreenshotScenario
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("wide", 1642, 958),
        ("desktop", 1500, 1000),
        ("compact", 1100, 800),
        ("minimum", 980, 640)
    ];

    public static int Run(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                foreach (var viewport in Viewports)
                    await CaptureViewport(outputDirectory, viewport.Name, viewport.Width, viewport.Height);
                Console.WriteLine("PASS compact table captures: Investigation and Workbench rename, Properties, Variables, CSV and review results at all four approved extents.");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { app.Shutdown(result); }
        };
        app.Run();
        return result;
    }

    private static async Task CaptureViewport(string root, string name, int width, int height)
    {
        string directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        InvestigationWorkspace workspace = await RetailScreenshotScenario.CreateWorkspaceAsync();
        NativeWindowSizeOverride? sizeOverride = null;
        var window = new InvestigationWindow(workspace)
        {
            Width = width, Height = height, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0
        };
        window.SourceInitialized += (_, _) => sizeOverride = NativeWindowSizeOverride.Install(window, width, height);
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.ContentRendered += (_, _) => rendered.TrySetResult(true);
        window.Show();
        try
        {
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            window.Width = width;
            window.Height = height;
            await IdleAsync(window);
            Capture(window, directory, "investigation-table", width, height);
            Require(window.FindName("MessageGrid") is DataGrid messages && messages.Items.Count > 0,
                "Investigation must render a populated message table.");

            ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
            var view = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
            Idle(window);

            RenameStates(window, view, directory, width, height);
            PropertyStates(window, view, directory, width, height);
            VariableStates(window, view, directory, width, height);
            await PrepareAndResults(window, view, directory, width, height);
        }
        finally
        {
            window.Close();
            sizeOverride?.Dispose();
            await workspace.DisposeAsync();
        }
        Console.WriteLine($"PASS {name} {width}x{height}: routed capture states asserted.");
    }

    private static void RenameStates(Window window, MessageLibraryPrototypeView view, string directory, int width, int height)
    {
        Click(view, "LibraryRename");
        var input = Elements(view).OfType<TextBox>().Single(element =>
            AutomationProperties.GetAutomationId(element) == "LibraryRenameInput" && element.IsVisible);
        input.Text = "Order created compact";
        Require(FindButton(view, "LibraryRenameSave").IsVisible && FindButton(view, "LibraryRenameCancel").IsVisible,
            "Inline rename must expose explicit Save name and Cancel actions.");
        Capture(window, directory, "rename-editing", width, height);
        Click(view, "LibraryRenameCancel");
        Require(((TextBlock)view.FindName("AuthorTitle")!).Text == "Order created",
            "Cancel must preserve the original template name.");
        Click(view, "LibraryRename");
        input = Elements(view).OfType<TextBox>().Single(element =>
            AutomationProperties.GetAutomationId(element) == "LibraryRenameInput" && element.IsVisible);
        input.Text = "Order created compact";
        Click(view, "LibraryRenameSave");
        Idle(window);
        Require(((TextBlock)view.FindName("AuthorTitle")!).Text == "Order created compact",
            "Save name must commit the new template name.");
        Capture(window, directory, "rename-saved", width, height);
    }

    private static void PropertyStates(Window window, MessageLibraryPrototypeView view, string directory, int width, int height)
    {
        Click(view, "LibraryEditorProperties");
        Idle(window);
        var grid = (DataGrid)view.FindName("ApplicationPropertiesGrid")!;
        Require(grid.Items.Count >= 3, "The Properties table must contain the sample rows.");
        grid.ScrollIntoView(CollectionView.NewItemPlaceholder);
        grid.UpdateLayout();
        Idle(window);
        int placeholderIndex = grid.Items.IndexOf(CollectionView.NewItemPlaceholder);
        Require(placeholderIndex >= 0, "The permanent Properties placeholder must remain in the grid.");
        var placeholderRow = grid.ItemContainerGenerator.ContainerFromIndex(placeholderIndex) as DataGridRow
            ?? throw new InvalidOperationException("The Properties placeholder row must be realized.");
        var placeholderLabel = Elements(placeholderRow).OfType<TextBlock>().SingleOrDefault(text =>
            text.Text == "+ Add property..." && text.IsVisible);
        Require(placeholderRow.IsVisible && placeholderLabel is not null,
            "The visible permanent row must prompt '+ Add property...'.");
        AssertFirstPropertyNameAlignsWithHeader(grid, window);
        Capture(window, directory, "properties", width, height);
        grid.SelectedIndex = 1;
        Idle(window);
        var selectedRow = (DataGridRow?)grid.ItemContainerGenerator.ContainerFromIndex(1);
        Require(selectedRow is not null && Elements(selectedRow).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "LibraryDeleteProperty").IsEnabled,
            "Delete property must be enabled for a selected row.");
        Capture(window, directory, "properties-selected", width, height);
        Require(grid.CanUserAddRows, "The Properties grid must retain its permanent placeholder row.");
        grid.ScrollIntoView(CollectionView.NewItemPlaceholder);
        grid.UpdateLayout();
        grid.CurrentCell = new DataGridCellInfo(CollectionView.NewItemPlaceholder, grid.Columns[0]);
        Require(grid.BeginEdit(), "The permanent Properties placeholder must enter edit mode.");
        Idle(window);
        grid.UpdateLayout();
        var newRow = grid.ItemContainerGenerator.ContainerFromIndex(grid.Items.Count - 2) as DataGridRow
            ?? throw new InvalidOperationException("The new Properties row must be realized for editing.");
        var newName = Elements(newRow).OfType<TextBox>().Single(editor =>
            AutomationProperties.GetAutomationId(editor) == "LibraryPropertyName");
        newName.Text = "compactCapture";
        Require(grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true),
            "The newly added Properties row must commit through the grid editor.");
        Require(grid.Items.Cast<object>().Count(item => !ReferenceEquals(item, CollectionView.NewItemPlaceholder)) == 4,
            "Committing the placeholder must create exactly one Properties row.");
        Idle(window);
        grid.UpdateLayout();
        var committedRow = grid.ItemContainerGenerator.ContainerFromIndex(grid.Items.Count - 2) as DataGridRow
            ?? throw new InvalidOperationException("The committed Properties row must be realized for display proof.");
        Require(Elements(committedRow).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == "compactCapture"),
            "The committed Name cell must display compactCapture instead of the add-property prompt.");
        int visiblePlaceholderCount = Elements(grid).OfType<TextBlock>().Count(text =>
            text.IsVisible && text.Text == "+ Add property...");
        Require(visiblePlaceholderCount == 1,
            "Exactly the permanent final row must display the add-property prompt after committing a row.");
        Capture(window, directory, "properties-new-row", width, height);
        SetPropertyValue(grid, 0, new string('L', 88));
        Capture(window, directory, "properties-long-value", width, height);
        CaptureScrollEndpoints(window, view, directory, "PropertiesEditorSurface", "properties", width, height);
    }

    private static void AssertFirstPropertyNameAlignsWithHeader(DataGrid grid, Window window)
    {
        grid.ScrollIntoView(grid.Items[0]);
        grid.UpdateLayout();
        var firstRow = grid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow
            ?? throw new InvalidOperationException("The first Properties row must be realized for alignment proof.");
        var nameHeader = Elements(grid).OfType<DataGridColumnHeader>().Single(header =>
            ReferenceEquals(header.Column, grid.Columns[0]));
        var headerLabel = Elements(nameHeader).OfType<TextBlock>().Single(text => text.Text == "Name");
        var nameCell = Elements(firstRow).OfType<DataGridCell>().Single(cell =>
            ReferenceEquals(cell.Column, grid.Columns[0]));
        var nameText = Elements(nameCell).OfType<TextBlock>().Single(text => !string.IsNullOrWhiteSpace(text.Text));
        Rect headerBounds = headerLabel.TransformToAncestor(window).TransformBounds(new Rect(headerLabel.RenderSize));
        Rect nameBounds = nameText.TransformToAncestor(window).TransformBounds(new Rect(nameText.RenderSize));
        Require(Math.Abs(headerBounds.Left - nameBounds.Left) <= 2,
            "The first property name must align with the Name column header.");
    }

    private static void VariableStates(Window window, MessageLibraryPrototypeView view, string directory, int width, int height)
    {
        Click(view, "LibraryEditorVariables");
        Idle(window);
        var grid = (DataGrid)view.FindName("VariablesGrid")!;
        grid.SelectedIndex = 0;
        Idle(window);
        var details = (FrameworkElement)view.FindName("VariableDefaultEditor")!;
        Require(details.IsVisible && ((CheckBox)view.FindName("UseVariableDefault")!).IsEnabled,
            "Input variables must show an available default toggle.");
        Capture(window, directory, "variables-input", width, height);
        grid.SelectedIndex = 2;
        Idle(window);
        Require(details.IsVisible && !((CheckBox)view.FindName("UseVariableDefault")!).IsEnabled
            && !((TextBox)view.FindName("VariableDefaultValue")!).IsEnabled
            && ((FrameworkElement)view.FindName("VariableDefaultHint")!).IsVisible,
            "Generated variables must keep their detail area visible and disabled.");
        Capture(window, directory, "variables-generated", width, height);
        CaptureScrollEndpoints(window, view, directory, "VariablesEditorSurface", "variables", width, height);
    }

    private static void CaptureScrollEndpoints(
        Window window, MessageLibraryPrototypeView view, string directory,
        string scrollViewerName, string statePrefix, int width, int height)
    {
        if (width != 980) return;

        var scrollViewer = (ScrollViewer)view.FindName(scrollViewerName)!;
        scrollViewer.ScrollToVerticalOffset(0);
        Idle(window);
        if (scrollViewer.ScrollableHeight <= 0) return;

        RequirePrimaryActionVisible(window, view);
        Capture(window, directory, $"{statePrefix}-scroll-top", width, height);
        scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight);
        Idle(window);
        Require(Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < 1,
            $"{scrollViewerName} must reach its lower scroll endpoint.");
        RequirePrimaryActionVisible(window, view);
        Capture(window, directory, $"{statePrefix}-scroll-bottom", width, height);
        scrollViewer.ScrollToVerticalOffset(0);
        Idle(window);
    }

    private static void RequirePrimaryActionVisible(Window window, MessageLibraryPrototypeView view)
    {
        var action = (FrameworkElement)view.FindName("ContinueToPrepareButton")!;
        Require(action.IsVisible && action.ActualHeight > 0,
            "The primary Prepare message action must remain visible while editor content scrolls.");
        Point bottom = action.TranslatePoint(new Point(0, action.ActualHeight), window);
        Require(bottom.Y <= window.ActualHeight,
            "The primary Prepare message action must remain within the visible window bounds.");
    }

    private static async Task PrepareAndResults(Window window, MessageLibraryPrototypeView view, string directory, int width, int height)
    {
        Click(view, "LibraryEditorBody");
        ((JsonEditor)view.FindName("EditorText")!).Text = "{\n  \"reference\": \"" + new string('R', 180) + "\"\n}";
        Click(view, "LibraryContinueToPrepare");
        Idle(window);
        var csv = (DataGrid)view.FindName("CsvRowsGrid")!;
        Require(csv.Items.Count == 3, "Prepare must render the three-row CSV preview table.");
        Capture(window, directory, "csv-validation", width, height);
        var validation = (Button)view.FindName("ValidationDetailsButton")!;
        Require(!validation.IsEnabled, "Valid rows without retained results must not open result details.");

        SelectSampleCsv(window, view, invalid: true);
        Idle(window);
        Capture(window, directory, "csv-invalid", width, height);
        Require(validation.IsEnabled && validation.Content?.ToString() == "View errors",
            "Invalid CSV rows must enable the validation details route.");
        CaptureOwnedDialog(window, validation, Path.Combine(directory, "validation-details.png"), dialog =>
        {
            Require(dialog.FindName("ValidationGrid") is DataGrid rows && rows.Items.Count == 3,
                "Validation details must show all three CSV rows.");
            dialog.Close();
        });
        SelectSampleCsv(window, view, invalid: false);
        Idle(window);

        ((RadioButton)view.FindName("SingleMode")!).IsChecked = true;
        Idle(window);
        var reviewButton = (Button)view.FindName("ReviewButton")!;
        Require(reviewButton.IsEnabled, "Valid single-message inputs must reach review.");
        reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, reviewButton));
        Idle(window);
        var review = (MessageLibraryPrototypeReviewSurface)((ContentControl)view.FindName("ReviewHost")!).Content;
        var confirm = (Button)review.FindName("ConfirmDispatch")!;
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirm));
        await WaitUntilAsync(window, () => ((FrameworkElement)review.FindName("ResultsSurface")!).IsVisible, TimeSpan.FromSeconds(12));
        var resultGrid = (DataGrid)review.FindName("ResultsGrid")!;
        Require(resultGrid.Items.Count == 1, "The routed send simulation must produce one result row.");
        Capture(window, directory, "results", width, height);

        var back = Elements(review).OfType<Button>().Single(button => button.Content?.ToString() == "Back to preparation");
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, back));
        Idle(window);
        reviewButton = (Button)view.FindName("ReviewButton")!;
        reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, reviewButton));
        Idle(window);
        review = (MessageLibraryPrototypeReviewSurface)((ContentControl)view.FindName("ReviewHost")!).Content;
        var schedule = Elements(review).OfType<RadioButton>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "PrototypeScheduleLater");
        schedule.IsChecked = true;
        ((DatePicker)review.FindName("ScheduleDateInput")!).SelectedDate = DateTime.Today.AddDays(2);
        ((TextBox)review.FindName("ScheduleTimeInput")!).Text = "23:59";
        var scheduleNow = (Button)review.FindName("ConfirmDispatch")!;
        Require(scheduleNow.IsEnabled, "The valid future schedule must enable its action.");
        scheduleNow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, scheduleNow));
        await WaitUntilAsync(window, () => ((FrameworkElement)review.FindName("ResultsSurface")!).IsVisible, TimeSpan.FromSeconds(12));
        Capture(window, directory, "scheduled-results", width, height);
        ((Button)review.FindName("CancelScheduled")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Idle(window);
        var scheduledGrid = (DataGrid)review.FindName("ScheduledGrid")!;
        Require(((FrameworkElement)review.FindName("CancellationSurface")!).IsVisible && scheduledGrid.Items.Count == 1,
            "Scheduled results must open the cancellation table with the retained receipt.");
        Capture(window, directory, "scheduled-table", width, height);
        var scheduledRow = scheduledGrid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow
            ?? throw new InvalidOperationException("The scheduled receipt row must be realized.");
        var selected = Elements(scheduledRow).OfType<CheckBox>().Single();
        selected.IsChecked = false;
        selected.IsChecked = true;
        Idle(window);
        var cancel = Elements(review).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "PrototypeCancelScheduled");
        Require(cancel.IsEnabled, "Selecting an eligible scheduled receipt must enable cancellation.");
        CaptureOwnedDialog(window, cancel, Path.Combine(directory, "cancellation-confirmation.png"), dialog =>
        {
            var confirmCancellation = (Button)dialog.FindName("ConfirmCancellationButton")!;
            confirmCancellation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirmCancellation));
        });
        Require(((FrameworkElement)review.FindName("HistorySurface")!).IsVisible
            && ((DataGrid)review.FindName("HistoryGrid")!).Items.Count == 1,
            "Confirmed cancellation must reach the actual history table.");
        Capture(window, directory, "cancellation-history", width, height);
    }

    private static void SelectSampleCsv(Window owner, MessageLibraryPrototypeView view, bool invalid)
    {
        var browse = FindButton(view, "LibraryBrowseCsv");
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window dialog = owner.OwnedWindows.OfType<Window>().Single();
            ((ComboBox)dialog.FindName("SampleCsvPicker")!).SelectedIndex = invalid ? 1 : 0;
            var useSample = Elements(dialog).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "PrototypeUseSampleCsv");
            useSample.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, useSample));
        }));
        browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, browse));
        Idle(owner);
    }

    private static void CaptureOwnedDialog(Window owner, Button action, string outputPath, Action<Window> actAfterCapture)
    {
        Exception? failure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Window? dialog = null;
            try
            {
                dialog = owner.OwnedWindows.OfType<Window>().Single();
                Idle(dialog);
                Require(dialog.IsVisible, "The owned dialog must be visible before capture.");
                var content = (FrameworkElement)dialog.Content;
                Require(content.ActualWidth > 0 && content.ActualHeight > 0,
                    "The owned dialog must have arranged content before capture.");
                WpfScreenshot.SaveWindowContent(dialog, outputPath,
                    (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
                actAfterCapture(dialog);
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }));
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, action));
        if (failure is not null) throw new InvalidOperationException("Could not complete the routed dialog capture.", failure);
        Idle(owner);
    }

    private static void SetPropertyValue(DataGrid grid, int rowIndex, string value)
    {
        object row = grid.Items[rowIndex];
        grid.ScrollIntoView(row);
        grid.SelectedItem = row;
        grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[2]);
        Require(grid.BeginEdit(), "The property value cell must enter edit mode.");
        Idle(Window.GetWindow(grid)!);
        var editor = Elements(grid).OfType<TextBox>().FirstOrDefault(item => item.IsVisible && item.IsEnabled)
            ?? throw new InvalidOperationException("The property value editor must be available.");
        editor.Text = value;
        Require(grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true),
            "The long property value must commit to the table row.");
        Idle(Window.GetWindow(grid)!);
    }

    private static Button FindButton(DependencyObject root, string id) => Elements(root).OfType<Button>().Single(button =>
        button.IsVisible && AutomationProperties.GetAutomationId(button) == id);

    private static void Click(DependencyObject root, string id)
    {
        var button = FindButton(root, id);
        Require(button.IsEnabled, $"Action {id} must be enabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        if (Window.GetWindow(root) is { } window) Idle(window);
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Capture(Window window, string directory, string state, int width, int height)
    {
        Idle(window);
        WpfScreenshot.SaveWindowContent(window, Path.Combine(directory, $"{state}.png"), width, height);
    }

    private static async Task WaitUntilAsync(Window window, Func<bool> condition, TimeSpan timeout)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > timeout) throw new TimeoutException("The requested routed state did not appear before the capture deadline.");
            await Task.Delay(40);
        }
        Idle(window);
    }

    private static void Idle(Window window) => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
    private static Task IdleAsync(Window window) => window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle).Task;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
