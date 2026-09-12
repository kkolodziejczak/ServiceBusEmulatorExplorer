using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class WatchProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        var workspace = window.Workspace;
        var originalEntity = workspace.SelectedEntity!;
        var originalFocus = workspace.FocusedMessage;
        var target = workspace.Roots.SelectMany(PrototypeData.Flatten)
            .First(node => !node.IsGroup && node.Kind != "Topic" && node.Path != originalEntity.Path);
        window.SetWatched(target.Path, true, true);
        window.SetWatched(target.Path, false, true);
        await Settle();
        Check(Notification() is null, "Enabling Watch starts silently without notifying for existing messages", report);
        Check(((TextBlock)window.FindName("WatchButtonLabel")).Text == "Watch", "Watch label reflects the selected entity rather than other watched entities", report);
        var before = workspace.SnapshotMessages().Count;
        window.SimulateWatchedArrivals();
        window.SimulateWatchedArrivals();
        await Settle();
        Check(workspace.SnapshotMessages().Count == before + 4, "Independent Watch generates Active and DLQ arrivals for an unselected entity", report);
        Check(workspace.SelectedEntity == originalEntity && workspace.FocusedMessage == originalFocus, "Background arrivals preserve the investigation scope and focused message", report);
        var notification = Notification() ?? throw new InvalidOperationException("Watch notification did not open.");
        Check(notification.Owner is null && notification.IsVisible && notification.Topmost && !notification.ShowInTaskbar,
            "Watch notification is a separate visible desktop window, independent of the main window", report);
        Check(Text(notification).Contains("2 new active messages") && Text(notification).Contains("1 other watched location"),
            "Repeated arrivals group by entity and Active/DLQ bucket in one persistent notification", report);
        ProofCapture.CheckBounds(notification, ProofCapture.Descendants(notification).OfType<Button>());
        ProofCapture.Save(notification, output, "watch-desktop-notification");
        var close = ProofCapture.Descendants(notification).OfType<Button>().Single(button => button.ToolTip?.ToString() == "Dismiss notification");
        var hoverKey = typeof(UIElement).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(DependencyPropertyKey)).Select(field => (DependencyPropertyKey)field.GetValue(null)!)
            .Single(key => key.DependencyProperty == UIElement.IsMouseOverProperty);
        close.SetValue(hoverKey, true);
        await Settle();
        var closeSurface = (Border)close.Template.FindName("Surface", close);
        Check(closeSurface.Background is System.Windows.Media.SolidColorBrush hover && hover.Color == System.Windows.Media.Color.FromRgb(234, 244, 255)
            && close.ActualWidth == close.ActualHeight, "Notification close uses a square target and soft blue hover (simulated pointer state)", report);
        ProofCapture.Save(notification, output, "watch-close-hover");
        close.SetValue(hoverKey, false);
        window.Hide();
        await Task.Delay(300);
        await Settle();
        Check(notification.IsVisible && !window.IsVisible, "Desktop notification remains visible while the application is hidden", report);
        var expected = workspace.SnapshotMessages().Where(row => row.Source == target.Path && !row.IsDeadLetter)
            .OrderByDescending(row => row.Enqueued).First();
        Click(notification, "Investigate");
        await Settle();
        for (var page = 0; workspace.IsSearching && page < 1000; page++) workspace.ScanNext();
        await Settle();
        Check(window.IsVisible && window.WindowState != WindowState.Minimized && workspace.IsCorrelationSearch
            && workspace.FocusedMessage?.Key == expected.Key,
            "Investigate restores the app, searches across the connection, and focuses the latest notified message", report);
        var notifiedCorrelations = workspace.SnapshotMessages().Where(row => row.Source == target.Path && !row.IsDeadLetter)
            .OrderByDescending(row => row.Enqueued).Take(2).Select(row => row.CorrelationId).Distinct().ToArray();
        Check(notifiedCorrelations.All(id => ((TextBox)window.FindName("SearchBox")).Text.Contains(id))
            && workspace.Messages.All(row => notifiedCorrelations.Contains(row.CorrelationId)),
            "Grouped notification searches all distinct notified correlation IDs without unrelated results", report);
        Check(workspace.Messages.Count == workspace.SnapshotMessages().Count(row => notifiedCorrelations.Contains(row.CorrelationId)),
            "Notification investigation includes every matching active and DLQ message across the connection", report);
        notification = Notification() ?? throw new InvalidOperationException("Remaining DLQ notification missing.");
        Check(Text(notification).Contains("2 new dead-letter messages"), "Responding advances to the next pending watched bucket", report);
        var snapshot = workspace.SnapshotMessages().Select(row => row.Key).ToArray();
        Click(notification, "Dismiss");
        await Settle();
        Check(Notification() is null && snapshot.SequenceEqual(workspace.SnapshotMessages().Select(row => row.Key)),
            "Dismiss closes the final notification without removing any messages", report);
        workspace.ToggleConnection();
        window.SimulateWatchedArrivals();
        await Settle();
        Check(Notification() is null && workspace.SnapshotMessages().Count == before + 4,
            "Watch pauses arrivals and notifications while disconnected", report);
        workspace.ToggleConnection();
        window.SetWatched(target.Path, true, false);
        window.SetWatched(target.Path, false, false);
        window.SimulateWatchedArrivals();
        await Settle();
        Check(Notification() is null && workspace.SnapshotMessages().Count == before + 4,
            "Disabling all watched buckets stops new arrivals", report);
        window.SetWatched(target.Path, true, true);
        window.SetWatched(target.Path, false, true);
        window.SimulateWatchedArrivals();
        await Settle();
        window.SetWatched(target.Path, true, false);
        await Settle();
        Check(Notification() is { } remaining && Text(remaining).Contains("1 new active message"),
            "Stopping one watched bucket clears its pending notification and advances to the remaining bucket", report);
        window.SetWatched(target.Path, false, false);
        await Settle();
        Check(Notification() is null && ((TextBlock)window.FindName("WatchButtonLabel")).Text == "Watch",
            "Stopping the final bucket closes pending notifications and restores the Watch label", report);

        var closingWindow = new PrototypeWindow();
        closingWindow.ConfigureApplicationLifetime(true);
        closingWindow.Show();
        closingWindow.SetWatched(closingWindow.Workspace.EntityPath, true, true);
        closingWindow.SimulateWatchedArrivals();
        await Settle();
        Check(Notification() is not null, "Proof lifetime can exercise a real desktop notification without starting tray services", report);
        closingWindow.Close();
        await Settle();
        Check(Notification() is null && !Application.Current.Windows.Cast<Window>().Contains(closingWindow),
            "Closing in proof mode closes the app window and all owned watch resources without lingering windows", report);
        if (originalFocus is not null) window.OpenWatchedMessage(originalFocus);
        else workspace.SelectEntity(originalEntity);
        window.Activate();
        await Settle();
    }

    private static WatchNotificationWindow? Notification() => Application.Current.Windows.OfType<WatchNotificationWindow>().SingleOrDefault();
    private static string Text(Window window) => string.Join("\n", ProofCapture.Descendants(window).OfType<TextBlock>().Select(block => block.Text));
    private static void Click(Window window, string label) => ProofCapture.Descendants(window).OfType<Button>()
        .First(button => System.Windows.Automation.AutomationProperties.GetName(button) == label + " notification").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
