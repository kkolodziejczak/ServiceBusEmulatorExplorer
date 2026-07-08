using Azure;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public sealed class ServiceBusAdministrationService(IServiceBusClientFactory clientFactory) : IServiceBusAdministrationService
{
    public async Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
    {
        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        var nodes = new List<ServiceBusEntityNode>();

        await AddQueuesAsync(client, nodes, cancellationToken);
        await AddTopicsAndSubscriptionsAsync(client, nodes, cancellationToken);

        return EntityTreeBuilder.SortForNavigation(nodes);
    }

    public async Task CreateQueueAsync(CreateQueueCommand command, CancellationToken cancellationToken)
    {
        EnsureValid(EntityManagementCommandValidator.Validate(command));

        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        CreateQueueOptions options = EntityManagementRequestMapper.ToCreateQueueOptions(command);
        await client.CreateQueueAsync(options, cancellationToken);
    }

    public async Task UpdateQueueAsync(UpdateQueueCommand command, CancellationToken cancellationToken)
    {
        EnsureValid(EntityManagementCommandValidator.Validate(command));

        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        QueueProperties properties = await GetValueAsync(client.GetQueueAsync(command.Name.Trim(), cancellationToken));
        EntityManagementRequestMapper.ApplyQueueUpdate(properties, command);
        await client.UpdateQueueAsync(properties, cancellationToken);
    }

    public async Task DeleteQueueAsync(string name, CancellationToken cancellationToken)
    {
        EnsureName(name, "Queue name");

        await clientFactory.AdministrationClient.DeleteQueueAsync(name.Trim(), cancellationToken);
    }

    public async Task CreateTopicAsync(CreateTopicCommand command, CancellationToken cancellationToken)
    {
        EnsureValid(EntityManagementCommandValidator.Validate(command));

        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        CreateTopicOptions options = EntityManagementRequestMapper.ToCreateTopicOptions(command);
        await client.CreateTopicAsync(options, cancellationToken);
    }

    public async Task UpdateTopicAsync(UpdateTopicCommand command, CancellationToken cancellationToken)
    {
        EnsureValid(EntityManagementCommandValidator.Validate(command));

        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        TopicProperties properties = await GetValueAsync(client.GetTopicAsync(command.Name.Trim(), cancellationToken));
        EntityManagementRequestMapper.ApplyTopicUpdate(properties, command);
        await client.UpdateTopicAsync(properties, cancellationToken);
    }

    public async Task DeleteTopicAsync(string name, CancellationToken cancellationToken)
    {
        EnsureName(name, "Topic name");

        await clientFactory.AdministrationClient.DeleteTopicAsync(name.Trim(), cancellationToken);
    }

    public async Task CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken)
    {
        EnsureValid(EntityManagementCommandValidator.Validate(command));

        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        CreateSubscriptionOptions options = EntityManagementRequestMapper.ToCreateSubscriptionOptions(command);
        await client.CreateSubscriptionAsync(options, cancellationToken);
    }

    public async Task UpdateSubscriptionAsync(UpdateSubscriptionCommand command, CancellationToken cancellationToken)
    {
        EnsureValid(EntityManagementCommandValidator.Validate(command));

        ServiceBusAdministrationClient client = clientFactory.AdministrationClient;
        SubscriptionProperties properties = await GetValueAsync(client.GetSubscriptionAsync(
            command.TopicName.Trim(),
            command.SubscriptionName.Trim(),
            cancellationToken));
        EntityManagementRequestMapper.ApplySubscriptionUpdate(properties, command);
        await client.UpdateSubscriptionAsync(properties, cancellationToken);
    }

    public async Task DeleteSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        EnsureName(topicName, "Topic name");
        EnsureName(subscriptionName, "Subscription name");

        await clientFactory.AdministrationClient.DeleteSubscriptionAsync(
            topicName.Trim(),
            subscriptionName.Trim(),
            cancellationToken);
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

    private static void EnsureValid(ServiceBusEmulatorExplorer.Core.Connection.ValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors));
        }
    }

    private static void EnsureName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.");
        }
    }
}
