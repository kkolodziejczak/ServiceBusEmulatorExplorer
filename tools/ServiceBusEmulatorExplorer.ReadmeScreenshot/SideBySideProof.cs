using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class SideBySideProof
{
    public static async Task RunAsync(InvestigationWindow window, InvestigationWorkspace workspace, string outputDirectory)
    {
        foreach ((double width, double height, string suffix) in new[]
        {
            (1500d, 1000d, "1500"),
            (1100d, 800d, "1100"),
            (980d, 640d, "980")
        })
        {
            window.Width = width;
            window.Height = height;
            await SettleAsync(window);
            AssertSideBySide(window, suffix);
            AssertControlsFit(window, suffix);
            if (width == 980) AssertFiveRowsRemainVisible(window);
            WpfScreenshot.SaveWindowContent(window,
                Path.Combine(Path.GetFullPath(outputDirectory), $"workbench-{suffix}.png"),
                (int)width, (int)height);
            AssertSplitterCanResize(window);
            await AssertInspectorInteractionsAsync(window, workspace);
            await AssertActivityLogAndDeadLetterReplayAsync(window, workspace);
            Require<ToggleButton>(window, "ActiveTab").IsChecked = true;
            await SettleAsync(window);
            Require<DataGrid>(window, "MessageGrid").SelectedItem = workspace.Browse.Messages
                .Single(row => row.MessageId == "order-10482-dispatched");
            await SettleAsync(window);
        }

        window.Width = 1500;
        window.Height = 1000;
        await SettleAsync(window);
        AssertSideBySide(window, "resized back to 1500");
        Console.WriteLine("Side-by-side proof passed at 1500, 1100, and 980px; five 50px rows remain visible at 980x640.");
    }

    private static void AssertSideBySide(InvestigationWindow window, string viewport)
    {
        Grid list = Require<Grid>(window, "ListPane");
        Grid inspector = Require<Grid>(window, "InspectorPane");
        GridSplitter splitter = Require<GridSplitter>(window, "InspectorSplitter");
        if (Grid.GetRow(list) != 0 || Grid.GetRow(inspector) != 0
            || Grid.GetColumn(inspector) <= Grid.GetColumn(list)
            || splitter.ResizeDirection != GridResizeDirection.Columns
            || inspector.ActualWidth <= 0 || list.ActualWidth <= 0)
        {
            throw new InvalidOperationException(
                $"At {viewport}, ListPane is row {Grid.GetRow(list)} ({list.ActualWidth:F0}px) and " +
                $"InspectorPane is row {Grid.GetRow(inspector)}, column {Grid.GetColumn(inspector)} " +
                $"({inspector.ActualWidth:F0}px); splitter direction is {splitter.ResizeDirection}.");
        }
        DataGrid messageGrid = Require<DataGrid>(window, "MessageGrid");
        Console.WriteLine($"Layout {viewport}: list={list.ActualWidth:F0}px, inspector={inspector.ActualWidth:F0}px, " +
            $"grid={messageGrid.ActualWidth:F0}px, columns={string.Join(", ", messageGrid.Columns.Select(column =>
                $"{column.Header as string ?? "Select"}:{column.ActualWidth:F0}/{column.Width}/{column.Visibility}"))}.");
    }

    private static void AssertControlsFit(InvestigationWindow window, string viewport)
    {
        Grid list = Require<Grid>(window, "ListPane");
        Grid inspector = Require<Grid>(window, "InspectorPane");
        foreach (string name in new[] { "WatchButton", "RefreshButton", "AutoInterval", "PauseButton" })
            AssertInside(Require<FrameworkElement>(window, name), list, name, viewport);
        foreach (string name in new[]
        {
            "JsonTab", "PropertiesTab", "RawTab", "SaveInspectedTemplateButton",
            "FindButton", "CopyCorrelationButton", "FindRelatedButton"
        })
            AssertInside(Require<FrameworkElement>(window, name), inspector, name, viewport);
        FrameworkElement replay = Require<FrameworkElement>(window, "ReplayButton");
        if (replay.IsVisible) AssertInside(replay, inspector, "ReplayButton", viewport);
    }

    private static void AssertInside(FrameworkElement control, FrameworkElement pane, string name, string viewport)
    {
        Rect bounds = control.TransformToAncestor(pane).TransformBounds(new Rect(new Point(), control.RenderSize));
        if (!control.IsVisible || bounds.Width <= 0 || bounds.Height <= 0
            || bounds.Left < -1 || bounds.Top < -1
            || bounds.Right > pane.ActualWidth + 1 || bounds.Bottom > pane.ActualHeight + 1)
        {
            throw new InvalidOperationException(
                $"At {viewport}, '{name}' is hidden or clipped: bounds={bounds}, pane={pane.ActualWidth:F0}x{pane.ActualHeight:F0}.");
        }
    }

    private static void AssertFiveRowsRemainVisible(InvestigationWindow window)
    {
        DataGrid grid = Require<DataGrid>(window, "MessageGrid");
        ScrollContentPresenter viewport = Descendants<ScrollContentPresenter>(grid)
            .FirstOrDefault(presenter => presenter.IsVisible && presenter.ActualHeight > 0)
            ?? throw new InvalidOperationException("The message grid has no visible row viewport.");
        DataGridRow[] rows = Enumerable.Range(0, grid.Items.Count)
            .Select(index => grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow)
            .Where(row => row is { IsVisible: true })
            .Cast<DataGridRow>()
            .Where(row =>
            {
                Rect bounds = row.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(), row.RenderSize));
                return bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1;
            })
            .ToArray();
        if (Require<FrameworkElement>(window, "LogPanel").Visibility != Visibility.Collapsed
            || Math.Abs(grid.RowHeight - 50) > 1 || rows.Length < 5
            || rows.Take(5).Any(row => Math.Abs(row.ActualHeight - 50) > 1))
        {
            throw new InvalidOperationException(
                $"At 980x640 with the log collapsed, only {rows.Length} visible rows of " +
                $"{grid.RowHeight:F0}px ({string.Join(", ", rows.Select(row => row.ActualHeight.ToString("F0")))}) fit.");
        }
    }

    private static void AssertSplitterCanResize(InvestigationWindow window)
    {
        GridSplitter splitter = Require<GridSplitter>(window, "InspectorSplitter");
        Grid list = Require<Grid>(window, "ListPane");
        double startingListWidth = list.ActualWidth;
        _ = Keyboard.Focus(splitter);
        PresentationSource source = PresentationSource.FromVisual(splitter)
            ?? throw new InvalidOperationException("The message inspector splitter has no presentation source.");
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Left)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Source = splitter
        };
        splitter.RaiseEvent(key);
        window.UpdateLayout();
        double resizedWidth = list.ActualWidth;
        var restore = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Right)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Source = splitter
        };
        splitter.RaiseEvent(restore);
        window.UpdateLayout();
        if (Math.Abs(resizedWidth - startingListWidth) < 1 || Math.Abs(list.ActualWidth - startingListWidth) > 2)
            throw new InvalidOperationException($"A routed left/right key did not resize and restore the splitter ({startingListWidth:F0}, {resizedWidth:F0}, {list.ActualWidth:F0}).");
    }

    private static async Task AssertInspectorInteractionsAsync(InvestigationWindow window, InvestigationWorkspace workspace)
    {
        DataGrid grid = Require<DataGrid>(window, "MessageGrid");
        string originalId = workspace.Browse.FocusedMessage?.MessageId
            ?? throw new InvalidOperationException("The starting message selection is missing.");
        int originalIndex = grid.Items.Cast<MessageRow>().ToList().FindIndex(row => row.MessageId == originalId);
        grid.SelectedIndex = originalIndex == 0 ? 1 : 0;
        await SettleAsync(window);
        if (workspace.Browse.FocusedMessage?.MessageId != ((MessageRow)grid.SelectedItem).MessageId
            || Require<TextBlock>(window, "InspectorTitle").Text != workspace.Browse.FocusedMessage?.EventName)
            throw new InvalidOperationException("Selecting another message did not update the focused inspector message.");
        grid.SelectedIndex = originalIndex;
        await SettleAsync(window);

        foreach ((string tabName, string bodyName, Visibility expected) in new[]
        {
            ("JsonTab", "BodyEditor", Visibility.Visible),
            ("PropertiesTab", "BodyViewer", Visibility.Visible),
            ("RawTab", "BodyViewer", Visibility.Visible)
        })
        {
            Require<ToggleButton>(window, tabName).IsChecked = true;
            await SettleAsync(window);
            if (Require<FrameworkElement>(window, bodyName).Visibility != expected)
                throw new InvalidOperationException($"The {tabName} action did not display its inspector body.");
        }

        Button find = Require<Button>(window, "FindButton");
        find.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, find));
        await SettleAsync(window);
        FrameworkElement findPanel = Require<FrameworkElement>(window, "FindPanel");
        if (!findPanel.IsVisible || !Require<TextBox>(window, "FindBox").IsVisible)
            throw new InvalidOperationException("The Find action did not open its search panel.");
        AssertInside(findPanel, Require<Grid>(window, "InspectorPane"), "FindPanel", "Find open");
        Button close = Descendants<Button>(findPanel).First(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Close find");
        close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, close));
        await SettleAsync(window);
        if (findPanel.IsVisible) throw new InvalidOperationException("The close action did not dismiss the Find panel.");
        Require<ToggleButton>(window, "JsonTab").IsChecked = true;
        await SettleAsync(window);
    }

    private static async Task AssertActivityLogAndDeadLetterReplayAsync(InvestigationWindow window, InvestigationWorkspace workspace)
    {
        Button logToggle = Require<Button>(window, "LogToggle");
        FrameworkElement logPanel = Require<FrameworkElement>(window, "LogPanel");
        logToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, logToggle));
        await SettleAsync(window);
        if (logPanel.Visibility != Visibility.Visible)
            throw new InvalidOperationException("The activity log did not expand through its routed toggle action.");
        AssertSideBySide(window, "expanded log");
        AssertControlsFit(window, "expanded log");
        logToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, logToggle));
        await SettleAsync(window);
        if (logPanel.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The activity log did not collapse through its routed toggle action.");

        Require<ToggleButton>(window, "DeadLetterTab").IsChecked = true;
        for (int attempt = 0; attempt < 20 && workspace.Browse.Messages.Count == 0; attempt++)
        {
            await Task.Delay(5);
            await SettleAsync(window);
        }
        FrameworkElement replay = Require<FrameworkElement>(window, "ReplayButton");
        if (!workspace.Browse.IsDeadLetter || workspace.Browse.FocusedMessage?.IsDeadLetter != true || !replay.IsVisible)
            throw new InvalidOperationException("Selecting the synthetic DLQ delivery did not expose the real Replay action.");
        await SettleAsync(window);
        AssertControlsFit(window, "DLQ replay");
    }

    private static async Task SettleAsync(FrameworkElement root)
    {
        root.UpdateLayout();
        await root.Dispatcher.InvokeAsync(root.UpdateLayout, DispatcherPriority.ContextIdle);
    }

    private static T Require<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"The rendered window is missing '{name}'.");

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
