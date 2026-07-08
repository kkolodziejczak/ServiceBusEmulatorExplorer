using Azure;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public sealed class ServiceBusAdministrationService(IServiceBusClientFactory clientFactory) : IServiceBusEntityBrowser
{
    public async Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
    {
        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        var nodes = new List<ServiceBusEntityNode>();

        await AddQueuesAsync(client, nodes, cancellationToken);
        await AddTopicsAndSubscriptionsAsync(client, nodes, cancellationToken);

        return EntityTreeBuilder.SortForNavigation(nodes);
    }

    private static async Task AddQueuesAsync(
        ServiceBusAdministrationClient client,
        List<ServiceBusEntityNode> nodes,
        CancellationToken cancellationToken)
    {
        await foreach (QueueProperties queue in client.GetQueuesAsync(cancellationToken).WithCancellation(cancellationToken))
        {
            QueueRuntimeProperties runtime = await GetValueAsync(
                client.GetQueueRuntimePropertiesAsync(queue.Name, cancellationToken));

            nodes.Add(EntityTreeBuilder.CreateNode(CreateQueueSource(queue, runtime)));
        }
    }

    private static EntityTreeSource CreateQueueSource(
        QueueProperties queue,
        QueueRuntimeProperties runtime)
    {
        return new EntityTreeSource(
            EntityKind.Queue,
            queue.Name,
            TopicName: null,
            runtime.ActiveMessageCount,
            runtime.DeadLetterMessageCount,
            runtime.ScheduledMessageCount,
            queue.Status.ToString(),
            runtime.CreatedAt,
            runtime.UpdatedAt,
            queue.LockDuration,
            queue.MaxDeliveryCount,
            queue.DefaultMessageTimeToLive,
            queue.RequiresSession,
            queue.RequiresDuplicateDetection);
    }

    private static async Task AddTopicsAndSubscriptionsAsync(
        ServiceBusAdministrationClient client,
        List<ServiceBusEntityNode> nodes,
        CancellationToken cancellationToken)
    {
        await foreach (TopicProperties topic in client.GetTopicsAsync(cancellationToken).WithCancellation(cancellationToken))
        {
            TopicRuntimeProperties runtime = await GetValueAsync(
                client.GetTopicRuntimePropertiesAsync(topic.Name, cancellationToken));

            nodes.Add(EntityTreeBuilder.CreateNode(CreateTopicSource(topic, runtime)));
            await AddSubscriptionsAsync(client, topic.Name, nodes, cancellationToken);
        }
    }

    private static EntityTreeSource CreateTopicSource(
        TopicProperties topic,
        TopicRuntimeProperties runtime)
    {
        return new EntityTreeSource(
            EntityKind.Topic,
            topic.Name,
            TopicName: null,
            ActiveMessageCount: 0,
            DeadLetterMessageCount: 0,
            runtime.ScheduledMessageCount,
            topic.Status.ToString(),
            runtime.CreatedAt,
            runtime.UpdatedAt,
            LockDuration: null,
            MaxDeliveryCount: null,
            topic.DefaultMessageTimeToLive,
            RequiresSession: null,
            topic.RequiresDuplicateDetection);
    }

    private static async Task AddSubscriptionsAsync(
        ServiceBusAdministrationClient client,
        string topicName,
        List<ServiceBusEntityNode> nodes,
        CancellationToken cancellationToken)
    {
        await foreach (SubscriptionProperties subscription in client.GetSubscriptionsAsync(topicName, cancellationToken).WithCancellation(cancellationToken))
        {
            SubscriptionRuntimeProperties runtime = await GetValueAsync(
                client.GetSubscriptionRuntimePropertiesAsync(topicName, subscription.SubscriptionName, cancellationToken));

            nodes.Add(EntityTreeBuilder.CreateNode(CreateSubscriptionSource(topicName, subscription, runtime)));
        }
    }

    private static EntityTreeSource CreateSubscriptionSource(
        string topicName,
        SubscriptionProperties subscription,
        SubscriptionRuntimeProperties runtime)
    {
        return new EntityTreeSource(
            EntityKind.Subscription,
            subscription.SubscriptionName,
            topicName,
            runtime.ActiveMessageCount,
            runtime.DeadLetterMessageCount,
            ScheduledMessageCount: 0,
            subscription.Status.ToString(),
            runtime.CreatedAt,
            runtime.UpdatedAt,
            subscription.LockDuration,
            subscription.MaxDeliveryCount,
            subscription.DefaultMessageTimeToLive,
            subscription.RequiresSession,
            RequiresDuplicateDetection: null);
    }

    private static async Task<T> GetValueAsync<T>(Task<Response<T>> responseTask)
    {
        Response<T> response = await responseTask;
        return response.Value;
    }

}
