using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class RetailScreenshotScenario
{
    public static async Task<ShellViewModel> CreateViewModelAsync()
    {
        IReadOnlyList<ServiceBusEntityNode> entities = RetailScreenshotData.CreateEntities();
        var administrationService = new ScenarioAdministrationService(entities);
        var messageService = new ScenarioMessageService(RetailScreenshotData.CreateMessages());
        var viewModel = new ShellViewModel(
            new ScenarioProfileStore(),
            new ScenarioClientFactory(),
            administrationService,
            messageService,
            new UnsupportedDeadLetterReplayService(),
            new UnsupportedEntityManagementWorkflow(),
            new TopicSubscriptionRefreshWorkflow(administrationService, messageService),
            new UnsupportedMessageDialogService(),
            new ScenarioClock());

        await viewModel.LoadProfilesAsync();
        await viewModel.ConnectCommand.ExecuteAsync(null);

        EntityTreeNodeViewModel orderTopic = FindEntityNode(
            viewModel.EntityTree,
            entity => entity.Kind == EntityKind.Topic && entity.Name == "order-events");
        viewModel.SelectEntity(orderTopic);
        await viewModel.RefreshSelectedTopicSubscriptionsCommand.ExecuteAsync(null);

        ExplorerMessage dispatched = viewModel.MessageInspection.ActiveMessages.First(
            message => message.MessageId == "order-10482-dispatched");
        viewModel.MessageInspection.SelectActiveMessage(dispatched);
        ValidateScenario(viewModel);
        return viewModel;
    }

    private static EntityTreeNodeViewModel FindEntityNode(
        IEnumerable<EntityTreeNodeViewModel> nodes,
        Func<ServiceBusEntityNode, bool> predicate)
    {
        return FindEntityNodeOrDefault(nodes, predicate)
            ?? throw new InvalidOperationException("The retail screenshot entity was not loaded.");
    }

    private static EntityTreeNodeViewModel? FindEntityNodeOrDefault(
        IEnumerable<EntityTreeNodeViewModel> nodes,
        Func<ServiceBusEntityNode, bool> predicate)
    {
        foreach (EntityTreeNodeViewModel node in nodes)
        {
            if (node.Entity is not null && predicate(node.Entity))
            {
                return node;
            }

            EntityTreeNodeViewModel? childMatch = FindEntityNodeOrDefault(node.Children, predicate);
            if (childMatch is not null)
            {
                return childMatch;
            }
        }

        return null;
    }

    private static void ValidateScenario(ShellViewModel viewModel)
    {
        if (viewModel.SelectedEntityTitle != "order-events")
        {
            throw new InvalidOperationException("The README scenario did not select order-events.");
        }

        if (viewModel.MessageInspection.ActiveMessages.Count != 16)
        {
            throw new InvalidOperationException(
                $"The README scenario loaded {viewModel.MessageInspection.ActiveMessages.Count} messages instead of 16.");
        }

        if (!viewModel.MessageInspection.SelectedBody.Contains("\"eventType\": \"OrderDispatched\"", StringComparison.Ordinal)
            || !viewModel.MessageInspection.SelectedApplicationProperties.Contains("eventType: OrderDispatched", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The README scenario did not inspect the OrderDispatched event.");
        }
    }
}
