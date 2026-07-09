using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
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

            ExplorerMessage subscriptionMessage = await WaitForSingleMessageAsync(
                service,
                subscriptionAddress,
                MessageBucket.Active,
                TimeSpan.FromSeconds(10),
                testTimeout.Token);
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

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Replay_dlq_copy_leaves_original_until_explicit_delete()
    {
        string queueName = $"it-dlq-{Guid.NewGuid():N}".ToLowerInvariant();
        var adminClient = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await adminClient.CreateQueueAsync(queueName, testTimeout.Token);

        await using var factory = new DirectServiceBusClientFactory();
        await factory.ConnectAsync(
            new ConnectionProfile(
                "integration",
                ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString),
            testTimeout.Token);
        var messageService = new ServiceBusMessageService(factory);
        var replayService = new DeadLetterReplayService(factory);

        try
        {
            var address = new EntityAddress(EntityKind.Queue, queueName);
            await messageService.SendMessageAsync(
                new SendMessageCommand(
                    address,
                    "poison body",
                    ContentType: "text/plain",
                    CorrelationId: "correlation-dlq",
                    Subject: "poison",
                    ApplicationProperties: new Dictionary<string, object?> { ["kind"] = "poison" }),
                testTimeout.Token);
            await DeadLetterMessagesAsync(factory.RuntimeClient, queueName, count: 1, testTimeout.Token);

            ExplorerMessage original = Assert.Single(await messageService.PeekMessagesAsync(
                address,
                MessageBucket.DeadLetter,
                take: 10,
                fromSequenceNumber: null,
                testTimeout.Token));

            ReplayResult replayResult = await replayService.ReplayAsync(
                new ReplayRequest(
                    address,
                    address,
                    original.SequenceNumber,
                    EditedBody: "replayed body",
                    ContentType: original.ContentType,
                    CorrelationId: original.CorrelationId,
                    SessionId: original.SessionId,
                    Subject: original.Subject,
                    original.ApplicationProperties,
                    Core.Messaging.ReplayIdPolicy.NewGuid,
                    DeleteOriginal: false),
                testTimeout.Token);

            ExplorerMessage replayed = Assert.Single(await messageService.PeekMessagesAsync(
                address,
                MessageBucket.Active,
                take: 10,
                fromSequenceNumber: null,
                testTimeout.Token));
            ExplorerMessage remainingDlq = Assert.Single(await messageService.PeekMessagesAsync(
                address,
                MessageBucket.DeadLetter,
                take: 10,
                fromSequenceNumber: null,
                testTimeout.Token));

            Assert.False(replayResult.OriginalDeleted);
            Assert.NotEqual(original.MessageId, replayResult.NewMessageId);
            Assert.Equal(replayResult.NewMessageId, replayed.MessageId);
            Assert.Equal("replayed body", replayed.Body);
            Assert.Equal(original.SequenceNumber, remainingDlq.SequenceNumber);

            DeleteDeadLetterMessagesResult deleteResult = await replayService.DeleteAsync(
                new DeleteDeadLetterMessagesRequest(address, [original.SequenceNumber]),
                testTimeout.Token);

            Assert.Equal(1, deleteResult.DeletedCount);
            Assert.Empty(await messageService.PeekMessagesAsync(
                address,
                MessageBucket.DeadLetter,
                take: 10,
                fromSequenceNumber: null,
                testTimeout.Token));
        }
        finally
        {
            if (await adminClient.QueueExistsAsync(queueName, CancellationToken.None))
            {
                await adminClient.DeleteQueueAsync(queueName, CancellationToken.None);
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

    private static async Task<ExplorerMessage> WaitForSingleMessageAsync(
        ServiceBusMessageService service,
        EntityAddress address,
        MessageBucket bucket,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            IReadOnlyList<ExplorerMessage> messages = await service.PeekMessagesAsync(
                address,
                bucket,
                take: 10,
                fromSequenceNumber: null,
                cancellationToken);
            if (messages.Count == 1)
            {
                return messages[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        IReadOnlyList<ExplorerMessage> finalMessages = await service.PeekMessagesAsync(
            address,
            bucket,
            take: 10,
            fromSequenceNumber: null,
            cancellationToken);
        return Assert.Single(finalMessages);
    }
}
