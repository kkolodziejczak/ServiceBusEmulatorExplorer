using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class RetailScreenshotScenario
{
    public static async Task<InvestigationWorkspace> CreateWorkspaceAsync()
    {
        var preferences = RetailScreenshotData.CreatePreferences();
        var messages = RetailScreenshotData.CreateMessages();
        var snapshot = RetailScreenshotData.CreateSnapshot();
        var workflow = new BrokerConnectionWorkflow(
            () => new ScenarioClientFactory(),
            _ => new ScenarioEntityBrowser(snapshot),
            _ => new ScenarioMessageService(messages));
        var workspace = new InvestigationWorkspace(new ScenarioWorkspacePreferencesStore(preferences), workflow);

        await workspace.InitializeAsync();
        await workspace.ConnectAsync();

        EntityNode orderEvents = workspace.Browse.AllEntities().Single(node =>
            node.Kind == nameof(EntityKind.Topic) && node.Name == "order-events");
        await workspace.Browse.SelectAsync(orderEvents, deadLetter: false);

        MessageRow selected = workspace.Browse.Messages.Single(row => row.MessageId == "order-10482-dispatched");
        workspace.Browse.FocusedMessage = selected;
        workspace.Activity.Clear();
        workspace.Activity.Add(new(
            RetailScreenshotData.ScenarioTime,
            "Connected to Demo retail workspace.",
            Warning: false,
            Watch: false));
        ValidateScenario(workspace);
        return workspace;
    }

    public static void ValidateRenderedWindow(InvestigationWindow window, InvestigationWorkspace workspace)
    {
        if (window is not InvestigationWindow)
        {
            throw new InvalidOperationException("The README scenario did not create the Investigation Workspace window.");
        }

        if (window.FindName("ConnectionSelector") is not FrameworkElement
            || window.FindName("MessageGrid") is not DataGrid messageGrid
            || window.FindName("InspectorTitle") is not FrameworkElement
            || window.FindName("BodyEditor") is not FrameworkElement
            || window.FindName("InspectorPane") is not FrameworkElement inspectorPane)
        {
            throw new InvalidOperationException(
                "The README scenario did not render the Investigation Workspace connection, message, and inspector surfaces.");
        }

        double messageColumnWidth = messageGrid.Columns[1].ActualWidth;
        FrameworkElement content = window.Content as FrameworkElement
            ?? throw new InvalidOperationException("The README scenario has no renderable window content.");
        DataGridColumn enqueuedColumn = messageGrid.Columns.Single(column =>
            string.Equals(column.Header?.ToString(), "Enqueued (UTC)", StringComparison.Ordinal));
        bool hasRealizedRow = messageGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow;
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        Console.WriteLine(
            $"Live screenshot layout: window {window.ActualWidth:F0}x{window.ActualHeight:F0}, " +
            $"content {content.ActualWidth:F0}x{content.ActualHeight:F0}, DPI {dpi.PixelsPerInchX:F0}, " +
            $"grid {messageGrid.ActualWidth:F0}, columns " +
            $"{string.Join(", ", messageGrid.Columns.Select(column => $"{column.Header as string ?? "Select"}={column.ActualWidth:F0}"))}.");

        if (!window.IsLoaded || !messageGrid.IsLoaded || !hasRealizedRow
            || Math.Abs(window.ActualWidth - WpfScreenshot.CaptureWidth) > 1
            || Math.Abs(window.ActualHeight - WpfScreenshot.CaptureHeight) > 1
            || Math.Abs(content.ActualWidth - WpfScreenshot.CaptureWidth) > 1
            || Math.Abs(content.ActualHeight - WpfScreenshot.CaptureHeight) > 1
            || Grid.GetColumn(inspectorPane) != 2
            || Grid.GetRow(inspectorPane) != 0
            || enqueuedColumn.Visibility != Visibility.Visible
            || messageGrid.ActualWidth < 600
            || Math.Abs(messageGrid.Columns[0].ActualWidth - 40) > 1
            || messageColumnWidth < 250
            || Math.Abs(messageGrid.Columns[2].ActualWidth - 145) > 1
            || Math.Abs(enqueuedColumn.ActualWidth - 140) > 1
            || inspectorPane.ActualWidth < 350)
        {
            throw new InvalidOperationException(
                $"The README scenario rendered a compact or unstable workspace: " +
                $"window {window.ActualWidth:F0}x{window.ActualHeight:F0}px, " +
                $"content {content.ActualWidth:F0}x{content.ActualHeight:F0}px, " +
                $"message grid {messageGrid.ActualWidth:F0}px, message column {messageColumnWidth:F0}px, " +
                $"inspector {inspectorPane.ActualWidth:F0}px, realized row {hasRealizedRow}.");
        }

        if (!workspace.IsConnected || workspace.Surface.Messages.Count != 16
            || workspace.Surface.FocusedMessage?.MessageId != "order-10482-dispatched")
        {
            throw new InvalidOperationException("The rendered README scenario lost its connected message inspection state.");
        }
    }

    private static void ValidateScenario(InvestigationWorkspace workspace)
    {
        if (!workspace.IsConnected || workspace.SelectedProfile.Connection.Name != "Demo retail workspace")
        {
            throw new InvalidOperationException("The README scenario did not connect the synthetic demo profile.");
        }

        if (workspace.Browse.SelectedEntity?.Path != "order-events"
            || workspace.Browse.Messages.Count != 16
            || workspace.Browse.FocusedMessage?.MessageId != "order-10482-dispatched")
        {
            throw new InvalidOperationException("The README scenario did not select the expected topic and message.");
        }

        if (!workspace.Inspector.RawText.Contains("OrderDispatched", StringComparison.Ordinal)
            || !workspace.Inspector.PropertiesText.Contains("eventType", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The README scenario did not populate the message inspector.");
        }
    }
}
