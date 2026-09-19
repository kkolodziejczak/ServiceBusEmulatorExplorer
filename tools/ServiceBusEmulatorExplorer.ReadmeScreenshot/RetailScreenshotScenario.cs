using System.Windows;
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
            || window.FindName("MessageGrid") is not FrameworkElement
            || window.FindName("InspectorTitle") is not FrameworkElement
            || window.FindName("BodyEditor") is not FrameworkElement)
        {
            throw new InvalidOperationException(
                "The README scenario did not render the Investigation Workspace connection, message, and inspector surfaces.");
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
