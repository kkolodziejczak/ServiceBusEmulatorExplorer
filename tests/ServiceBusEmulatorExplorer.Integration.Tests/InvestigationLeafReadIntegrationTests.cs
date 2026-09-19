using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationLeafReadIntegrationTests
{
    private const int FixtureCount = 60;
    private const int DeadLetterCount = 5;
    private const int ActiveCount = FixtureCount - DeadLetterCount;
    private const int PageSize = 50;

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Discovery_paging_and_bounded_search_are_source_aware_and_non_consuming()
    {
        string runId = Guid.NewGuid().ToString("N");
        string queueName = CreateEntityName("queue", runId);
        string topicName = CreateEntityName("topic", runId);
        string firstSubscriptionName = "first";
        string secondSubscriptionName = "second";
        var queueAddress = new EntityAddress(EntityKind.Queue, queueName);
        var firstAddress = new EntityAddress(EntityKind.Subscription, firstSubscriptionName, topicName);
        var secondAddress = new EntityAddress(EntityKind.Subscription, secondSubscriptionName, topicName);
        var sources = new[] { queueAddress, firstAddress, secondAddress };
        var adminClient = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        Exception? testFailure = null;

        try
        {
            await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
            await adminClient.CreateTopicAsync(topicName, testTimeout.Token);
            await adminClient.CreateSubscriptionAsync(topicName, firstSubscriptionName, testTimeout.Token);
            await adminClient.CreateSubscriptionAsync(topicName, secondSubscriptionName, testTimeout.Token);

            await using DirectServiceBusClientFactory factory = await ConnectFactoryAsync(testTimeout.Token);
            var service = new ServiceBusMessageService(factory);
            await SendFixturesAsync(factory.RuntimeClient, queueAddress, $"queue-{runId}", testTimeout.Token);
            await SendFixturesAsync(factory.RuntimeClient, new EntityAddress(EntityKind.Topic, topicName), $"topic-{runId}", testTimeout.Token);

            await MoveToDeadLetterAsync(factory.RuntimeClient, queueName, DeadLetterCount, testTimeout.Token);
            await MoveSubscriptionToDeadLetterAsync(
                factory.RuntimeClient,
                topicName,
                firstSubscriptionName,
                DeadLetterCount,
                testTimeout.Token);
            await MoveSubscriptionToDeadLetterAsync(
                factory.RuntimeClient,
                topicName,
                secondSubscriptionName,
                DeadLetterCount,
                testTimeout.Token);

            EntityDiscoverySnapshot snapshot = await new InvestigationEntityBrowser(factory).DiscoverAsync(testTimeout.Token);
            EntityObservation discoveredQueue = Assert.Single(snapshot.Entities,
                item => item.Entity.Kind == EntityKind.Queue && item.Entity.Name == queueName);
            EntityObservation discoveredTopic = Assert.Single(snapshot.Entities,
                item => item.Entity.Kind == EntityKind.Topic && item.Entity.Name == topicName);
            EntityObservation discoveredFirstSubscription = Assert.Single(snapshot.Entities,
                item => item.Entity.Kind == EntityKind.Subscription &&
                    item.Entity.Name == firstSubscriptionName && item.Entity.TopicName == topicName);

            Assert.True(snapshot.IsComplete);
            Assert.Equal(CountAvailability.Unavailable, discoveredQueue.Counts.Scheduled.Availability);
            Assert.Null(discoveredQueue.Counts.Scheduled.Value);
            Assert.Equal(CountAvailability.Unavailable, discoveredTopic.Counts.Scheduled.Availability);
            Assert.Null(discoveredTopic.Counts.Scheduled.Value);
            Assert.Equal(CountAvailability.NotSupported, discoveredFirstSubscription.Counts.Scheduled.Availability);
            EntityObservation discoveredSecondSubscription = Assert.Single(snapshot.Entities,
                item => item.Entity.Kind == EntityKind.Subscription &&
                    item.Entity.Name == secondSubscriptionName && item.Entity.TopicName == topicName);
            Assert.Equal(CountAvailability.NotSupported, discoveredSecondSubscription.Counts.Scheduled.Availability);

            IReadOnlyList<MessageDelivery> activeFirst = await ReadAllAsync(
                new DeliveryPager(service),
                sources,
                MessageBucket.Active,
                testTimeout.Token);
            Assert.Equal(ActiveCount * sources.Length, activeFirst.Count);
            AssertSourceCounts(activeFirst, sources, ActiveCount);

            IReadOnlyList<MessageDelivery> activeSecond = await ReadAllAsync(
                new DeliveryPager(service),
                sources,
                MessageBucket.Active,
                testTimeout.Token);
            Assert.Equal(activeFirst.Count, activeSecond.Count);
            Assert.Equal(DeliveryKeys(activeFirst), DeliveryKeys(activeSecond));

            IReadOnlyList<MessageDelivery> deadLetters = await ReadAllAsync(
                new DeliveryPager(service),
                sources,
                MessageBucket.DeadLetter,
                testTimeout.Token);
            Assert.Equal(DeadLetterCount * sources.Length, deadLetters.Count);
            AssertSourceCounts(deadLetters, sources, DeadLetterCount);
            IReadOnlyList<MessageDelivery> deadLettersAgain = await ReadAllAsync(
                new DeliveryPager(service),
                sources,
                MessageBucket.DeadLetter,
                testTimeout.Token);
            Assert.Equal(DeliveryKeys(deadLetters), DeliveryKeys(deadLettersAgain));

            Assert.True(MessageSearchQuery.TryParse("*", out MessageSearchQuery? query, out string parseError), parseError);
            var search = new DeliverySearch(service);
            search.Reset(generation: 3, sources, query!, defaultMessageId: true);
            DeliverySearchResult firstSearch = await search.ScanNextAsync(
                maxDeliveries: 10,
                timeBudget: TimeSpan.FromSeconds(30),
                testTimeout.Token);
            DeliverySearchResult continuedSearch = await search.ScanNextAsync(
                maxDeliveries: DeliverySearch.DefaultMaxDeliveries,
                timeBudget: TimeSpan.FromSeconds(30),
                testTimeout.Token);

            Assert.Equal(DeliverySearchStopReason.MaxDeliveries, firstSearch.StopReason);
            Assert.False(firstSearch.IsComplete);
            Assert.True(continuedSearch.IsComplete);
            Assert.Equal(DeliverySearchStopReason.Completed, continuedSearch.StopReason);
            var searchKeys = firstSearch.Matches.Concat(continuedSearch.Matches).Select(DeliveryKey).ToList();
            Assert.Equal(FixtureCount * sources.Length, searchKeys.Count);
            Assert.Equal(searchKeys.Count, searchKeys.Distinct().Count());
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await DeleteEntitiesAsync(adminClient, queueName, topicName, testFailure is null);
        }
    }

    private static async Task<DirectServiceBusClientFactory> ConnectFactoryAsync(CancellationToken cancellationToken)
    {
        var factory = new DirectServiceBusClientFactory();
        await factory.ConnectAsync(
            new ConnectionProfile(
                "investigation-leaf-read",
                ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString),
            cancellationToken);
        return factory;
    }

    private static async Task SendFixturesAsync(
        ServiceBusClient runtimeClient,
        EntityAddress address,
        string fixturePrefix,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = runtimeClient.CreateSender(address.Name);
        var messages = Enumerable.Range(0, FixtureCount)
            .Select(index => new ServiceBusMessage($"fixture|{fixturePrefix}|{index:D3}")
            {
                MessageId = $"{fixturePrefix}-{index:D3}",
                Subject = $"investigation-{fixturePrefix}"
            })
            .ToList();
        await sender.SendMessagesAsync(messages, cancellationToken);
    }

    private static async Task MoveToDeadLetterAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        int count,
        CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = runtimeClient.CreateReceiver(queueName);
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.ReceiveMessagesAsync(
            count,
            maxWaitTime: TimeSpan.FromSeconds(20),
            cancellationToken);
        Assert.Equal(count, messages.Count);
        foreach (ServiceBusReceivedMessage message in messages)
            await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
    }

    private static async Task MoveSubscriptionToDeadLetterAsync(
        ServiceBusClient runtimeClient,
        string topicName,
        string subscriptionName,
        int count,
        CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = runtimeClient.CreateReceiver(topicName, subscriptionName);
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.ReceiveMessagesAsync(
            count,
            maxWaitTime: TimeSpan.FromSeconds(20),
            cancellationToken);
        Assert.Equal(count, messages.Count);
        foreach (ServiceBusReceivedMessage message in messages)
            await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
    }

    private static async Task<IReadOnlyList<MessageDelivery>> ReadAllAsync(
        DeliveryPager pager,
        IReadOnlyList<EntityAddress> sources,
        MessageBucket bucket,
        CancellationToken cancellationToken)
    {
        pager.Reset(generation: 1, sources, bucket);
        var deliveries = new List<MessageDelivery>();
        for (int pageNumber = 0; pageNumber < 20 && pager.HasMore; pageNumber++)
        {
            IReadOnlyList<MessageDelivery> page = await pager.LoadNextAsync(PageSize, cancellationToken);
            if (page.Count == 0)
                break;
            deliveries.AddRange(page);
        }

        return deliveries;
    }

    private static void AssertSourceCounts(
        IReadOnlyList<MessageDelivery> deliveries,
        IReadOnlyList<EntityAddress> sources,
        int expectedCount)
    {
        Assert.Equal(deliveries.Count, deliveries.Select(DeliveryKey).Distinct().Count());
        foreach (EntityAddress source in sources)
        {
            Assert.Equal(
                expectedCount,
                deliveries.Count(delivery => delivery.Identity.Source == source));
        }
    }

    private static IEnumerable<string> DeliveryKeys(IEnumerable<MessageDelivery> deliveries) =>
        deliveries.Select(DeliveryKey);

    private static string DeliveryKey(MessageDelivery delivery) =>
        $"{delivery.Identity.Source.Kind}:{delivery.Identity.Source.TopicName}:{delivery.Identity.Source.Name}:{delivery.Identity.Bucket}:{delivery.Identity.SequenceNumber}";

    private static string CreateEntityName(string kind, string runId) =>
        $"it-leaf-{kind}-{runId}";

    private static async Task DeleteEntitiesAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        string topicName,
        bool failOnError)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            if (await adminClient.QueueExistsAsync(queueName, cleanupTimeout.Token))
                await adminClient.DeleteQueueAsync(queueName, cleanupTimeout.Token);
            if (await adminClient.TopicExistsAsync(topicName, cleanupTimeout.Token))
                await adminClient.DeleteTopicAsync(topicName, cleanupTimeout.Token);
        }
        catch when (!failOnError)
        {
        }
    }
}
