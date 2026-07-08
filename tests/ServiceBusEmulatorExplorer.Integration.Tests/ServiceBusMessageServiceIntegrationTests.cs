using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

public sealed class ServiceBusMessageServiceIntegrationTests
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Send_and_peek_active_dlq_and_topic_subscription_messages()
    {
        string queueName = $"it-msg-{Guid.NewGuid():N}".ToLowerInvariant();
        string topicName = $"it-topic-{Guid.NewGuid():N}".ToLowerInvariant();
        string subscriptionName = "sub";
        var adminClient = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(testTimeout.Token);
        await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
        await adminClient.CreateTopicAsync(topicName, testTimeout.Token);
        await adminClient.CreateSubscriptionAsync(topicName, subscriptionName, testTimeout.Token);

        await using var factory = new DirectServiceBusClientFactory();
        await factory.ConnectAsync(
            new ConnectionProfile(
                "integration",
                ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString),
            testTimeout.Token);
        var service = new ServiceBusMessageService(factory);

        try
        {
            var address = new EntityAddress(EntityKind.Queue, queueName);
            await service.SendMessageAsync(
                new SendMessageCommand(
                    address,
                    "hello integration 1",
                    ContentType: "text/plain",
                    CorrelationId: "correlation-it",
                    Subject: "active-1",
                    ApplicationProperties: new Dictionary<string, object?> { ["kind"] = "active" }),
                testTimeout.Token);
            await service.SendMessageAsync(
                new SendMessageCommand(
                    address,
                    "hello integration 2",
                    Subject: "active-2"),
                testTimeout.Token);

            IReadOnlyList<ExplorerMessage> activeMessages = await service.PeekMessagesAsync(
                address,
                MessageBucket.Active,
                take: 1,
                fromSequenceNumber: null,
                testTimeout.Token);

            ExplorerMessage activeMessage = Assert.Single(activeMessages);
            Assert.Equal("hello integration 1", activeMessage.Body);
            Assert.Equal("text/plain", activeMessage.ContentType);
            Assert.Equal("correlation-it", activeMessage.CorrelationId);
            Assert.Equal("active-1", activeMessage.Subject);
            Assert.Equal("active", activeMessage.ApplicationProperties["kind"]);

            IReadOnlyList<ExplorerMessage> nextActiveMessages = await service.PeekMessagesAsync(
                address,
                MessageBucket.Active,
                take: 1,
                activeMessage.SequenceNumber + 1,
                testTimeout.Token);

            ExplorerMessage nextActiveMessage = Assert.Single(nextActiveMessages);
            Assert.Equal("hello integration 2", nextActiveMessage.Body);

            await DeadLetterMessagesAsync(factory.RuntimeClient, queueName, count: 2, testTimeout.Token);

            IReadOnlyList<ExplorerMessage> deadLetterMessages = await service.PeekMessagesAsync(
                address,
                MessageBucket.DeadLetter,
                take: 1,
                fromSequenceNumber: null,
                testTimeout.Token);

            ExplorerMessage deadLetterMessage = Assert.Single(deadLetterMessages);
            Assert.Equal("hello integration 1", deadLetterMessage.Body);

            IReadOnlyList<ExplorerMessage> nextDeadLetterMessages = await service.PeekMessagesAsync(
                address,
                MessageBucket.DeadLetter,
                take: 1,
                deadLetterMessage.SequenceNumber + 1,
                testTimeout.Token);

            ExplorerMessage nextDeadLetterMessage = Assert.Single(nextDeadLetterMessages);
            Assert.Equal("hello integration 2", nextDeadLetterMessage.Body);

            var topicAddress = new EntityAddress(EntityKind.Topic, topicName);
            var subscriptionAddress = new EntityAddress(EntityKind.Subscription, subscriptionName, topicName);
            await service.SendMessageAsync(
                new SendMessageCommand(
                    topicAddress,
                    "topic integration",
                    Subject: "topic-subscription"),
                testTimeout.Token);

            IReadOnlyList<ExplorerMessage> subscriptionMessages = await service.PeekMessagesAsync(
                subscriptionAddress,
                MessageBucket.Active,
                take: 10,
                fromSequenceNumber: null,
                testTimeout.Token);

            ExplorerMessage subscriptionMessage = Assert.Single(subscriptionMessages);
            Assert.Equal("topic integration", subscriptionMessage.Body);
            Assert.Equal("topic-subscription", subscriptionMessage.Subject);
        }
        finally
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
    }

    private static async Task DeadLetterMessagesAsync(
        ServiceBusClient runtimeClient,
        string queueName,
        int count,
        CancellationToken cancellationToken)
    {
        await using ServiceBusReceiver receiver = runtimeClient.CreateReceiver(queueName);
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.ReceiveMessagesAsync(
            maxMessages: count,
            maxWaitTime: TimeSpan.FromSeconds(10),
            cancellationToken);

        Assert.Equal(count, messages.Count);
        foreach (ServiceBusReceivedMessage message in messages)
        {
            await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
        }
    }
}
