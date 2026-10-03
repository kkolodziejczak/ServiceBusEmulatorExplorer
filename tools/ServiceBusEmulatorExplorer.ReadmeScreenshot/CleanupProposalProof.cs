using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class CleanupProposalProof
{
    private static readonly (double Width, double Height)[] Viewports = [(1500, 1000), (1100, 800), (980, 640)];

    public static async Task RunAsync(InvestigationWindow window, InvestigationWorkspace workspace, string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        await SettleAsync(window);

        Require(((FrameworkElement)window.FindName("LogPanel")!).Visibility == Visibility.Collapsed,
            "Activity log must start collapsed in a fresh workspace.");
        Require(((TextBlock)window.FindName("LogHeading")!).Text == "Activity log · Collapsed",
            "The startup log heading must describe its collapsed state.");

        ((ToggleButton)window.FindName("MessageLibraryTab")!).IsChecked = true;
        await SettleAsync(window);
        var tree = (TreeView)window.FindName("NamespaceTree")!;
        foreach (var viewport in Viewports)
        {
            window.Width = viewport.Width;
            window.Height = viewport.Height;
            await SettleAsync(window);
            AssertNamespaceCountsAlign(tree, window);
        }
        SetSize(window, Viewports[0]);

        var view = (MessageLibraryPrototypeView)window.FindName("MessageLibraryPrototype")!;
        var editor = (JsonEditor)view.FindName("EditorText")!;
        string draft = editor.Text;
        var destination = (ComboBox)view.FindName("TemplateDestination")!;
        object? draftDestination = destination.SelectedItem;
        var search = (TextBox)window.FindName("SearchBox")!;
        var searchIcon = (ShapePath)window.FindName("SearchIcon")!;
        var clearSearch = (Button)window.FindName("ClearSearchButton")!;
        var copyButton = Descendants(view).OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "LibraryCopyAuthor");

        search.Text = "order-events";
        await SettleAsync(window);
        Require(searchIcon.Data?.ToString() == window.FindResource("FilterGeometry").ToString(),
            "A nonempty namespace query must display the funnel icon.");
        Require(ReferenceEquals(search.BorderBrush, window.FindResource("PrimaryBrush"))
                && ReferenceEquals(searchIcon.Stroke, window.FindResource("PrimaryBrush")),
            "The active namespace filter must outline and accent the existing search field.");
        Require(editor.Text == draft && ReferenceEquals(destination.SelectedItem, draftDestination),
            "Filtering namespaces must preserve the open message draft and destination.");
        Require(Descendants(copyButton).OfType<ShapePath>().Any(path => path.Data?.ToString() == window.FindResource("CopyGeometry").ToString()),
            "The author Copy button must render the registered Copy icon.");
        WpfScreenshot.SaveWindowContent(window, Path.Combine(outputDirectory, "workbench-compose-filtered.png"), 1500, 1000);

        clearSearch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clearSearch));
        await SettleAsync(window);
        Require(search.Text.Length == 0 && searchIcon.Data?.ToString() == window.FindResource("SearchGeometry").ToString(),
            "Clearing the namespace query must restore the neutral magnifier state.");
        Require(!ReferenceEquals(search.BorderBrush, window.FindResource("PrimaryBrush")),
            "Clearing the namespace query must remove the active outline.");
        Require(editor.Text == draft && ReferenceEquals(destination.SelectedItem, draftDestination),
            "Clearing namespace search must preserve the open message draft and destination.");

        var log = (FrameworkElement)window.FindName("LogPanel")!;
        var logToggle = (Button)window.FindName("LogToggle")!;
        logToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, logToggle));
        await SettleAsync(window);
        var clearLog = (Button)window.FindName("ClearLogButton")!;
        Require(clearLog.IsVisible && Descendants(clearLog).OfType<TextBlock>().Any(text => text.Text == "Clear log"),
            "Expanded Activity log must expose a labelled Clear log action.");
        Require(workspace.Activity.Count > 0 && clearLog.IsEnabled,
            "Clear log should be enabled when entries exist.");
        clearLog.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clearLog));
        await SettleAsync(window);
        Require(workspace.Activity.Count == 0 && !clearLog.IsEnabled,
            "Clear log must remove entries and become disabled when the log is empty.");
        Require(((TextBlock)window.FindName("LogEmpty")!).Text == "No activity yet"
                && ((TextBlock)window.FindName("LogEmpty")!).IsVisible,
            "An empty activity log must show its empty state.");
        Require(editor.Text == draft && ReferenceEquals(destination.SelectedItem, draftDestination)
                && workspace.Browse.FocusedMessage?.MessageId == "order-10482-dispatched",
            "Clearing Activity log must preserve the draft, destination and inspected broker message.");
        workspace.Activity.Add(new(RetailScreenshotData.ScenarioTime, "Synthetic proof entry retained only for rendering.", false, false));

        ((Button)view.FindName("ContinueToPrepareButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync(window);
        var details = (Button)view.FindName("ValidationDetailsButton")!;
        Require(details.Visibility == Visibility.Collapsed,
            "Valid input without a prior run must not show a results or errors action.");
        var delimiter = (ComboBox)view.FindName("CsvDelimiter")!;
        delimiter.SelectedIndex = 1;
        await SettleAsync(window);
        Require(details.Visibility == Visibility.Visible && details.Content?.ToString() == "View errors",
            "Invalid input must expose the View errors action.");
        Require(((Button)view.FindName("ViewRunResultsButton")!).Visibility == Visibility.Collapsed,
            "Validation errors must not appear as a prior run.");

        Require(log.Visibility == Visibility.Visible, "The activity log should remain expanded for the error-state board.");
        SetSize(window, Viewports[1]);
        await SettleAsync(window);
        WpfScreenshot.SaveWindowContent(window, Path.Combine(outputDirectory, "prepare-errors-activity.png"), 1100, 800);
        await OpenAndInspectDialogAsync(window, details, dialog =>
        {
            var grid = (DataGrid)dialog.FindName("ValidationGrid")!;
            Require(dialog.SizeToContent == SizeToContent.Height && dialog.MinHeight == 0,
                "Validation details must size to content while retaining a usable minimum.");
            Require(grid.Items.Count == 3 && grid.ActualHeight >= 100 && grid.IsVisible,
                "Validation expansion must show all three representative row results.");
            Require(((ScrollViewer)dialog.FindName("DialogScroll")!).ScrollableHeight <= 1,
                "Three validation rows must fit without unused fixed-height dialog space.");
        });

        SetSize(window, Viewports[0]);
        delimiter.SelectedIndex = 0;
        await SettleAsync(window);
        Require(details.Visibility == Visibility.Collapsed,
            "Correcting the invalid delimiter must remove the View errors action.");
        ((Button)view.FindName("ReviewButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync(window);
        var review = (MessageLibraryPrototypeReviewSurface)((ContentControl)view.FindName("ReviewHost")!).Content;
        ((Button)review.FindName("ConfirmDispatch")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(window, () => ((Button)view.FindName("ViewRunResultsButton")!).Visibility == Visibility.Visible,
            "The simulated run should retain results for a later review.");
        ((Button)view.FindName("PrepareStepButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync(window);
        var lastRun = (Button)view.FindName("ViewRunResultsButton")!;
        Require(lastRun.Content?.ToString() == "View last run" && lastRun.IsVisible && lastRun.IsEnabled,
            "A completed execution must expose a separate View last run action.");
        Require(details.Visibility == Visibility.Collapsed,
            "A valid preview with retained results must not conflate them with validation errors.");
        lastRun.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, lastRun));
        await SettleAsync(window);
        review = (MessageLibraryPrototypeReviewSurface)((ContentControl)view.FindName("ReviewHost")!).Content;
        Require(((FrameworkElement)review.FindName("ResultsSurface")!).IsVisible
                && ((DataGrid)review.FindName("ResultsGrid")!).Items.Count == 3,
            "View last run must reopen the retained simulated-send results.");

        SetSize(window, Viewports[0]);
        ((Button)view.FindName("PrepareStepButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync(window);
        var map = Descendants((DependencyObject)view.FindName("CsvInputs")!).OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == "LibraryMapColumns");
        await OpenAndInspectDialogAsync(window, map, dialog =>
        {
            var surface = (FrameworkElement)dialog.FindName("MappingSurface")!;
            var scroll = (ScrollViewer)dialog.FindName("DialogScroll")!;
            var columns = Descendants(surface).OfType<TextBlock>().Where(text =>
                text.Text is "Variable" or "Declared type" or "Input source" or "Value").ToArray();
            Require(dialog.SizeToContent == SizeToContent.Height && dialog.MinHeight == 0,
                "CSV mapping must size to its content without fixed blank height.");
            Require(columns.Length == 4 && columns.Select(Grid.GetColumn).Order().SequenceEqual([0, 1, 2, 3]),
                "The CSV mapping table must retain all four mapping columns.");
            Require(!Descendants(surface).OfType<TextBlock>().Any(text => text.Text.StartsWith("Input sources:", StringComparison.Ordinal)),
                "The redundant Input sources helper sentence must be removed.");
            Require(scroll.ScrollableHeight <= 1 && dialog.ActualHeight < 590,
                "The compact CSV mapping dialog must fit without a large unused area.");
            WpfScreenshot.SaveWindowContent(dialog,
                Path.Combine(outputDirectory, "mapping-dialog.png"),
                (int)Math.Ceiling(((FrameworkElement)dialog.Content).ActualWidth),
                (int)Math.Ceiling(((FrameworkElement)dialog.Content).ActualHeight), minimumBytes: 1_000);
            ((ComboBox)dialog.FindName("CustomerValue")!).Text = "";
            Descendants(dialog).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "PrototypeApplyMapping")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.UpdateLayout();
            var mappingError = (TextBlock)dialog.FindName("MappingError")!;
            Require(mappingError.IsVisible && mappingError.ActualHeight > 0
                    && dialog.ActualHeight < SystemParameters.WorkArea.Height
                    && scroll.ScrollableHeight <= 1,
                "Mapping validation must expand visibly and remain content-sized without clipping or oversized blank space.");
        });
        await VerifySimpleDialogSizingAsync(window);

        Console.WriteLine("PASS cleanup proposals: aligned counts, namespace filter/draft preservation, Copy icon, collapsed/clearable log, separated validation/run results, content-sized mapping and validation dialogs.");
    }

    private static void AssertNamespaceCountsAlign(TreeView tree, Window window)
    {
        TreeViewItem queueGroup = Item(tree, 0);
        TreeViewItem topicGroup = Item(tree, 1);
        queueGroup.IsExpanded = true;
        topicGroup.IsExpanded = true;
        tree.UpdateLayout();
        TreeViewItem queue = Item(queueGroup, 0);
        TreeViewItem topic = Item(topicGroup, 0);
        topic.IsExpanded = true;
        tree.UpdateLayout();
        TreeViewItem subscription = Item(topic, 0);
        double[] queueEdges = CountEdges(queue, window);
        double[] topicEdges = CountEdges(topic, window);
        double[] subscriptionEdges = CountEdges(subscription, window);
        if (queueEdges.Length != 3 || topicEdges.Length != 3 || subscriptionEdges.Length != 3)
            throw new InvalidOperationException("Queue, topic and subscription rows must each render three count columns.");
        for (int index = 0; index < 3; index++)
            Require(Math.Abs(queueEdges[index] - topicEdges[index]) <= 1
                    && Math.Abs(topicEdges[index] - subscriptionEdges[index]) <= 1,
                $"Namespace count column {index} must align for queues, topics and subscriptions at {window.Width:0}px.");
    }

    private static double[] CountEdges(TreeViewItem item, Window window)
    {
        var presenter = Descendants(item).OfType<ContentPresenter>().FirstOrDefault(candidate => ReferenceEquals(candidate.Content, item.Header))
            ?? throw new InvalidOperationException("A namespace row header presenter was not found.");
        var grid = Descendants(presenter).OfType<Grid>().First(candidate =>
            candidate.Children.OfType<TextBlock>().Count(text => Grid.GetColumn(text) is >= 1 and <= 3) == 3);
        var counts = grid.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) is >= 1 and <= 3)
            .OrderBy(Grid.GetColumn).ToArray();
        return counts.Select(text => text.TransformToAncestor(window).Transform(new Point(text.ActualWidth, 0)).X).ToArray();
    }

    private static TreeViewItem Item(ItemsControl parent, int index)
    {
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem
            ?? throw new InvalidOperationException($"Namespace tree item {index} was not realized.");
    }

    private static async Task OpenAndInspectDialogAsync(Window owner, Button trigger, Action<MessageLibraryPrototypeDialog> inspect)
    {
        Exception? failure = null;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            MessageLibraryPrototypeDialog? dialog = null;
            try
            {
                dialog = Application.Current.Windows.OfType<MessageLibraryPrototypeDialog>().Single();
                dialog.UpdateLayout();
                inspect(dialog);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                dialog?.Close();
                completed.TrySetResult(true);
            }
        }));
        trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, trigger));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (failure is not null) throw new InvalidOperationException("A routed Workbench dialog proof failed.", failure);
        await SettleAsync(owner);
    }

    private static async Task VerifySimpleDialogSizingAsync(Window owner)
    {
        foreach (PrototypeDialogMode mode in new[]
        {
            PrototypeDialogMode.SampleCsv,
            PrototypeDialogMode.DraftGuard,
            PrototypeDialogMode.CancellationConfirmation
        })
        {
            var dialog = new MessageLibraryPrototypeDialog(mode, "Demo retail workspace", "order-events", 3)
            {
                Owner = owner,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0
            };
            var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.ContentRendered += (_, _) => rendered.TrySetResult(true);
            dialog.Show();
            try
            {
                await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await SettleAsync(dialog);
                Require(dialog.SizeToContent == SizeToContent.Height && dialog.MinHeight == 0
                        && dialog.ActualHeight < SystemParameters.WorkArea.Height,
                    $"The {mode} dialog must fit its content within the available work area.");
                Require(((ScrollViewer)dialog.FindName("DialogScroll")!).ScrollableHeight <= 1,
                    $"The {mode} dialog should not leave unused fixed-height space or clip its simple content.");
            }
            finally { dialog.Close(); }
        }
    }

    private static async Task WaitUntilAsync(Window window, Func<bool> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(failure);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
        }
        await SettleAsync(window);
    }

    private static async Task SettleAsync(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void SetSize(Window window, (double Width, double Height) viewport)
    {
        window.Width = viewport.Width;
        window.Height = viewport.Height;
        window.UpdateLayout();
    }

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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
