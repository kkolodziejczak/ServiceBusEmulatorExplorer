using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public sealed class TopicSubscriptionRefreshWorkflow(
    IServiceBusAdministrationService administrationService,
    IServiceBusMessageService messageService) : ITopicSubscriptionRefreshWorkflow
{
    public async Task<TopicSubscriptionRefreshResult> RefreshAsync(
        string topicName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceBusEntityNode> entities = await administrationService.GetEntityTreeAsync(cancellationToken);
        IReadOnlyList<ServiceBusEntityNode> subscriptions = FindTopicSubscriptions(entities, topicName);
        List<RefreshedSubscriptionMessages> refreshedSubscriptions = [];
        List<TopicSubscriptionRefreshFailure> failures = [];

        foreach (ServiceBusEntityNode subscription in subscriptions)
        {
            try
            {
                refreshedSubscriptions.Add(await RefreshSubscriptionAsync(subscription, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new TopicSubscriptionRefreshFailure(subscription, ex.Message));
            }
        }

        return new TopicSubscriptionRefreshResult(entities, refreshedSubscriptions, failures);
    }

    private static IReadOnlyList<ServiceBusEntityNode> FindTopicSubscriptions(
        IReadOnlyList<ServiceBusEntityNode> entities,
        string topicName)
    {
        return entities
            .Where(entity => entity.Kind == EntityKind.Subscription
                && string.Equals(entity.TopicName, topicName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<RefreshedSubscriptionMessages> RefreshSubscriptionAsync(
        ServiceBusEntityNode subscription,
        CancellationToken cancellationToken)
    {
        EntityAddress address = new(EntityKind.Subscription, subscription.Name, subscription.TopicName);
        IReadOnlyList<ExplorerMessage> activeMessages = await messageService.PeekMessagesAsync(
            address,
            MessageBucket.Active,
            take: 50,
            fromSequenceNumber: null,
            cancellationToken);
        IReadOnlyList<ExplorerMessage> deadLetterMessages = await messageService.PeekMessagesAsync(
            address,
            MessageBucket.DeadLetter,
            take: 50,
            fromSequenceNumber: null,
            cancellationToken);

        return new RefreshedSubscriptionMessages(subscription, activeMessages, deadLetterMessages);
    }
}
