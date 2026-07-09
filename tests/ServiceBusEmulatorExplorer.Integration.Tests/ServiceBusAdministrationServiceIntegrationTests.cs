using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class ServiceBusAdministrationServiceIntegrationTests
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Entity_management_creates_updates_lists_and_deletes_queue_topic_and_subscription()
    {
        string queueName = CreateEntityName("queue");
        string topicName = CreateEntityName("topic");
        string subscriptionName = "sub";
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        await using var factory = new DirectServiceBusClientFactory();
        await factory.ConnectAsync(
            new ConnectionProfile(
                "integration",
                ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString),
            testTimeout.Token);
        var service = new ServiceBusAdministrationService(factory);

        try
        {
            await service.CreateQueueAsync(
                new CreateQueueCommand(
                    queueName,
                    LockDuration: TimeSpan.FromSeconds(30),
                    MaxDeliveryCount: 3,
                    DefaultMessageTimeToLive: TimeSpan.FromMinutes(30)),
                testTimeout.Token);
            await service.UpdateQueueAsync(
                new UpdateQueueCommand(
                    queueName,
                    LockDuration: TimeSpan.FromSeconds(45),
                    MaxDeliveryCount: 5,
                    DefaultMessageTimeToLive: TimeSpan.FromMinutes(45)),
                testTimeout.Token);

            await service.CreateTopicAsync(
                new CreateTopicCommand(
                    topicName,
                    DefaultMessageTimeToLive: TimeSpan.FromMinutes(30)),
                testTimeout.Token);
            await service.UpdateTopicAsync(
                new UpdateTopicCommand(topicName, DefaultMessageTimeToLive: TimeSpan.FromMinutes(45)),
                testTimeout.Token);
            await service.CreateSubscriptionAsync(
                new CreateSubscriptionCommand(
                    topicName,
                    subscriptionName,
                    LockDuration: TimeSpan.FromSeconds(30),
                    MaxDeliveryCount: 4,
                    DefaultMessageTimeToLive: TimeSpan.FromMinutes(30)),
                testTimeout.Token);
            await service.UpdateSubscriptionAsync(
                new UpdateSubscriptionCommand(
                    topicName,
                    subscriptionName,
                    LockDuration: TimeSpan.FromSeconds(45),
                    MaxDeliveryCount: 6,
                    DefaultMessageTimeToLive: TimeSpan.FromMinutes(45)),
                testTimeout.Token);

            IReadOnlyList<ServiceBusEntityNode> entities = await service.GetEntityTreeAsync(testTimeout.Token);

            ServiceBusEntityNode queue = Assert.Single(entities, entity => entity.Name == queueName);
            ServiceBusEntityNode topic = Assert.Single(entities, entity => entity.Name == topicName);
            ServiceBusEntityNode subscription = Assert.Single(
                entities,
                entity => entity.TopicName == topicName && entity.Name == subscriptionName);
            Assert.Equal(EntityKind.Queue, queue.Kind);
            Assert.Equal(TimeSpan.FromSeconds(45), queue.Metadata.LockDuration);
            Assert.Equal(5, queue.Metadata.MaxDeliveryCount);
            Assert.Equal(TimeSpan.FromMinutes(45), queue.Metadata.DefaultMessageTimeToLive);
            Assert.Equal(EntityKind.Topic, topic.Kind);
            Assert.Equal(TimeSpan.FromMinutes(45), topic.Metadata.DefaultMessageTimeToLive);
            Assert.Equal(EntityKind.Subscription, subscription.Kind);
            Assert.Equal(TimeSpan.FromSeconds(45), subscription.Metadata.LockDuration);
            Assert.Equal(6, subscription.Metadata.MaxDeliveryCount);
            Assert.Equal(TimeSpan.FromMinutes(45), subscription.Metadata.DefaultMessageTimeToLive);

            await service.DeleteSubscriptionAsync(topicName, subscriptionName, testTimeout.Token);
            await service.DeleteTopicAsync(topicName, testTimeout.Token);
            await service.DeleteQueueAsync(queueName, testTimeout.Token);

            entities = await service.GetEntityTreeAsync(testTimeout.Token);
            Assert.DoesNotContain(entities, entity => entity.Name == queueName);
            Assert.DoesNotContain(entities, entity => entity.Name == topicName);
            Assert.DoesNotContain(entities, entity => entity.TopicName == topicName && entity.Name == subscriptionName);
        }
        finally
        {
            await DeleteEntitiesWithoutThrowingAsync(factory.AdministrationClient, queueName, topicName);
        }
    }

    private static string CreateEntityName(string prefix)
    {
        return $"it-admin-{prefix}-{Guid.NewGuid():N}".ToLowerInvariant();
    }

    private static async Task DeleteEntitiesWithoutThrowingAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        string topicName)
    {
        try
        {
            if (await adminClient.QueueExistsAsync(queueName, CancellationToken.None))
            {
                await adminClient.DeleteQueueAsync(queueName, CancellationToken.None);
            }

            if (await adminClient.TopicExistsAsync(topicName, CancellationToken.None))
            {
                await adminClient.DeleteTopicAsync(topicName, CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort cleanup only; failing assertions should preserve the original test failure.
        }
    }
}
