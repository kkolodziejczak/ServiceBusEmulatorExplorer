using System.Text;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationPeekIntegrationTests
{
    private const int FixtureCount = 62;
    private const int DeadLetterFixtureCount = 2;
    private const int SecondSubscriptionDeadLetterFixtureCount = 3;
    private const int ExpectedActiveFixtureCount = FixtureCount - DeadLetterFixtureCount;
    private const int ExpectedSecondSubscriptionActiveFixtureCount =
        FixtureCount - SecondSubscriptionDeadLetterFixtureCount;
    private const int PageSize = 50;
    private readonly ITestOutputHelper output;

    public InvestigationPeekIntegrationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Queue_peek_pages_preserve_source_sequence_and_raw_body_without_consuming_messages()
    {
        string runId = Guid.NewGuid().ToString("N");
        string queueName = CreateEntityName("queue", runId);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        ServiceBusAdministrationClient adminClient = new(ServiceBusEmulatorEnvironment.AdminConnectionString);

        Exception? testFailure = null;

        try
        {
            await adminClient.CreateQueueAsync(queueName, testTimeout.Token);
            await using DirectServiceBusClientFactory factory = await ConnectFactoryAsync(testTimeout.Token);
            var service = new ServiceBusMessageService(factory);
            var address = new EntityAddress(EntityKind.Queue, queueName);
            IReadOnlyDictionary<string, byte[]> expectedBodies = await SendFixturesAsync(
                factory.RuntimeClient,
                address,
                source: "queue",
                runId,
                testTimeout.Token);

            IReadOnlyList<ServiceBusReceivedMessage> deadLetterFixtures = await MoveFixturesToDeadLetterAsync(
                factory.RuntimeClient,
                queueName,
                DeadLetterFixtureCount,
                testTimeout.Token);

            RuntimeCountsObservation countsBeforePeek = await ObserveQueueCountsAsync(
                adminClient,
                queueName,
                testTimeout.Token);
            WriteRuntimeCounts("queue", queueName, countsBeforePeek);

            IReadOnlyList<ExplorerMessage> activeFirstPeek = await WaitForPeekCountAsync(
                service,
                address,
                MessageBucket.Active,
                ExpectedActiveFixtureCount,
                testTimeout.Token);
            IReadOnlyList<ExplorerMessage> deadLetterFirstPeek = await WaitForPeekCountAsync(
                service,
                address,
                MessageBucket.DeadLetter,
                DeadLetterFixtureCount,
                testTimeout.Token);

            Assert.Equal(ExpectedActiveFixtureCount, activeFirstPeek.Count);
            Assert.Equal(DeadLetterFixtureCount, deadLetterFirstPeek.Count);
            Assert.All(activeFirstPeek, message => AssertFixtureMessage(message, expectedBodies, "queue"));
            Assert.All(deadLetterFirstPeek, message => AssertFixtureMessage(message, expectedBodies, "queue"));
            Assert.Equal(
                deadLetterFixtures.Select(message => message.MessageId).OrderBy(id => id),
                deadLetterFirstPeek.Select(message => message.MessageId).OrderBy(id => id));
            Assert.Equal(
                deadLetterFixtures.Select(message => message.SequenceNumber).OrderBy(sequence => sequence),
                deadLetterFirstPeek.Select(message => message.SequenceNumber).OrderBy(sequence => sequence));
            AssertStrictlyIncreasingSequenceNumbers(activeFirstPeek);
            AssertStrictlyIncreasingSequenceNumbers(deadLetterFirstPeek);

            // Runtime counts are observed evidence only. The emulator may update them asynchronously,
            // so message pages remain the source of truth for this contract.
            AssertRuntimeCountsAreNonNegative(countsBeforePeek);

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PeekMessagesAsync(
                address,
                MessageBucket.Active,
                PageSize,
                fromSequenceNumber: null,
                canceled.Token));

            IReadOnlyList<ExplorerMessage> activeSecondPeek = await PeekAllAsync(
                service,
                address,
                MessageBucket.Active,
                PageSize,
                testTimeout.Token);
            IReadOnlyList<ExplorerMessage> deadLetterSecondPeek = await PeekAllAsync(
                service,
                address,
                MessageBucket.DeadLetter,
                PageSize,
                testTimeout.Token);

            Assert.Equal(activeFirstPeek.Select(message => message.MessageId), activeSecondPeek.Select(message => message.MessageId));
            Assert.Equal(deadLetterFirstPeek.Select(message => message.MessageId), deadLetterSecondPeek.Select(message => message.MessageId));
            Assert.Equal(activeFirstPeek.Select(message => message.SequenceNumber), activeSecondPeek.Select(message => message.SequenceNumber));
            Assert.Equal(deadLetterFirstPeek.Select(message => message.SequenceNumber), deadLetterSecondPeek.Select(message => message.SequenceNumber));
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await DeleteQueueAsync(adminClient, queueName, failOnError: testFailure is null);
        }
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Topic_subscription_peek_pages_preserve_source_sequence_and_raw_body_for_each_subscription()
    {
        string runId = Guid.NewGuid().ToString("N");
        string topicName = CreateEntityName("topic", runId);
        const string firstSubscriptionName = "first";
        const string secondSubscriptionName = "second";
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        ServiceBusAdministrationClient adminClient = new(ServiceBusEmulatorEnvironment.AdminConnectionString);

        Exception? testFailure = null;

        try
        {
            await adminClient.CreateTopicAsync(topicName, testTimeout.Token);
            await adminClient.CreateSubscriptionAsync(topicName, firstSubscriptionName, testTimeout.Token);
            await adminClient.CreateSubscriptionAsync(topicName, secondSubscriptionName, testTimeout.Token);
            await using DirectServiceBusClientFactory factory = await ConnectFactoryAsync(testTimeout.Token);
            var service = new ServiceBusMessageService(factory);
            var topicAddress = new EntityAddress(EntityKind.Topic, topicName);
            var firstAddress = new EntityAddress(EntityKind.Subscription, firstSubscriptionName, topicName);
            var secondAddress = new EntityAddress(EntityKind.Subscription, secondSubscriptionName, topicName);
            IReadOnlyDictionary<string, byte[]> expectedBodies = await SendFixturesAsync(
                factory.RuntimeClient,
                topicAddress,
                source: "topic",
                runId,
                testTimeout.Token);

            IReadOnlyList<ServiceBusReceivedMessage> firstDeadLetterFixtures = await MoveSubscriptionFixturesToDeadLetterAsync(
                factory.RuntimeClient,
                topicName,
                firstSubscriptionName,
                DeadLetterFixtureCount,
                testTimeout.Token);
            IReadOnlyList<ServiceBusReceivedMessage> secondDeadLetterFixtures = await MoveSubscriptionFixturesToDeadLetterAsync(
                factory.RuntimeClient,
                topicName,
                secondSubscriptionName,
                SecondSubscriptionDeadLetterFixtureCount,
                testTimeout.Token);

            RuntimeCountsObservation firstCounts = await ObserveSubscriptionCountsAsync(
                adminClient,
                topicName,
                firstSubscriptionName,
                testTimeout.Token);
            RuntimeCountsObservation secondCounts = await ObserveSubscriptionCountsAsync(
                adminClient,
                topicName,
                secondSubscriptionName,
                testTimeout.Token);
            AssertRuntimeCountsAreNonNegative(firstCounts);
            AssertRuntimeCountsAreNonNegative(secondCounts);
            WriteRuntimeCounts("subscription", $"{topicName}/{firstSubscriptionName}", firstCounts);
            WriteRuntimeCounts("subscription", $"{topicName}/{secondSubscriptionName}", secondCounts);

            IReadOnlyList<ExplorerMessage> firstActive = await WaitForPeekCountAsync(
                service,
                firstAddress,
                MessageBucket.Active,
                ExpectedActiveFixtureCount,
                testTimeout.Token);
            IReadOnlyList<ExplorerMessage> secondActive = await WaitForPeekCountAsync(
                service,
                secondAddress,
                MessageBucket.Active,
                ExpectedSecondSubscriptionActiveFixtureCount,
                testTimeout.Token);
            IReadOnlyList<ExplorerMessage> firstDeadLetter = await WaitForPeekCountAsync(
                service,
                firstAddress,
                MessageBucket.DeadLetter,
                DeadLetterFixtureCount,
                testTimeout.Token);
            IReadOnlyList<ExplorerMessage> secondDeadLetter = await WaitForPeekCountAsync(
                service,
                secondAddress,
                MessageBucket.DeadLetter,
                SecondSubscriptionDeadLetterFixtureCount,
                testTimeout.Token);

            AssertSubscriptionMessages(firstActive, expectedBodies, ExpectedActiveFixtureCount);
            AssertSubscriptionMessages(secondActive, expectedBodies, ExpectedSecondSubscriptionActiveFixtureCount);
            AssertSubscriptionMessages(firstDeadLetter, expectedBodies, DeadLetterFixtureCount);
            AssertSubscriptionMessages(secondDeadLetter, expectedBodies, SecondSubscriptionDeadLetterFixtureCount);
            Assert.Equal(
                firstDeadLetterFixtures.Select(message => message.MessageId).OrderBy(id => id),
                firstDeadLetter.Select(message => message.MessageId).OrderBy(id => id));
            Assert.Equal(
                firstDeadLetterFixtures.Select(message => message.SequenceNumber).OrderBy(sequence => sequence),
                firstDeadLetter.Select(message => message.SequenceNumber).OrderBy(sequence => sequence));
            Assert.Equal(
                secondDeadLetterFixtures.Select(message => message.MessageId).OrderBy(id => id),
                secondDeadLetter.Select(message => message.MessageId).OrderBy(id => id));
            Assert.Equal(
                secondDeadLetterFixtures.Select(message => message.SequenceNumber).OrderBy(sequence => sequence),
                secondDeadLetter.Select(message => message.SequenceNumber).OrderBy(sequence => sequence));
            AssertStrictlyIncreasingSequenceNumbers(firstActive);
            AssertStrictlyIncreasingSequenceNumbers(secondActive);
            AssertStrictlyIncreasingSequenceNumbers(firstDeadLetter);
            AssertStrictlyIncreasingSequenceNumbers(secondDeadLetter);

            // Peeking twice proves that the active and DLQ fixture messages remain available.
            Assert.Equal(
                firstActive.Select(message => message.MessageId),
                (await PeekAllAsync(service, firstAddress, MessageBucket.Active, PageSize, testTimeout.Token))
                    .Select(message => message.MessageId));
            Assert.Equal(
                secondActive.Select(message => message.MessageId),
                (await PeekAllAsync(service, secondAddress, MessageBucket.Active, PageSize, testTimeout.Token))
                    .Select(message => message.MessageId));
            Assert.Equal(
                firstDeadLetter.Select(message => message.MessageId),
                (await PeekAllAsync(service, firstAddress, MessageBucket.DeadLetter, PageSize, testTimeout.Token))
                    .Select(message => message.MessageId));
            Assert.Equal(
                secondDeadLetter.Select(message => message.MessageId),
                (await PeekAllAsync(service, secondAddress, MessageBucket.DeadLetter, PageSize, testTimeout.Token))
                    .Select(message => message.MessageId));
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await DeleteTopicAsync(adminClient, topicName, failOnError: testFailure is null);
        }
    }

    private static async Task<DirectServiceBusClientFactory> ConnectFactoryAsync(CancellationToken cancellationToken)
    {
        var factory = new DirectServiceBusClientFactory();
        await factory.ConnectAsync(
            new ConnectionProfile(
                "investigation-peek",
                ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString),
            cancellationToken);
        return factory;
    }

    private static async Task<IReadOnlyDictionary<string, byte[]>> SendFixturesAsync(
        ServiceBusClient runtimeClient,
        EntityAddress address,
        string source,
        string runId,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = runtimeClient.CreateSender(address.Name);
        var expectedBodies = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var messages = new List<ServiceBusMessage>(FixtureCount);

        for (int index = 0; index < FixtureCount; index++)
        {
            string messageId = $"sbe-investigation-{source}-{runId}-{index:D3}";
            string body = $"fixture|source={source}|run={runId}|index={index:D3}|π|終";
            // Keep one deliberately non-UTF8 body in the fixture to prove RawBody survives
            // independently of the replacement-character display text.
            byte[] bodyBytes = index == 0
                ? [0x66, 0x69, 0x78, 0x74, 0x75, 0x72, 0x65, 0xFF, 0x00, 0x80]
                : Encoding.UTF8.GetBytes(body);
            var message = new ServiceBusMessage(BinaryData.FromBytes(bodyBytes))
            {
                MessageId = messageId,
                Subject = $"investigation-{source}",
                ContentType = "text/plain; charset=utf-8"
            };
            message.ApplicationProperties["fixture"] = "investigation-peek";
            message.ApplicationProperties["source"] = source;
            expectedBodies.Add(messageId, bodyBytes);
            messages.Add(message);
        }

        await sender.SendMessagesAsync(messages, cancellationToken);
        return expectedBodies;
    }

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> MoveFixturesToDeadLetterAsync(
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
        {
            await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
        }

        return messages;
    }

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> MoveSubscriptionFixturesToDeadLetterAsync(
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
        {
            await receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken);
        }

        return messages;
    }

    private static async Task<IReadOnlyList<ExplorerMessage>> WaitForPeekCountAsync(
        IServiceBusMessageService service,
        EntityAddress address,
        MessageBucket bucket,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<ExplorerMessage> messages = await PeekAllAsync(
                service,
                address,
                bucket,
                PageSize,
                cancellationToken);
            if (messages.Count >= expectedCount)
            {
                return messages;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<ExplorerMessage>> PeekAllAsync(
        IServiceBusMessageService service,
        EntityAddress address,
        MessageBucket bucket,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var messages = new List<ExplorerMessage>();
        long? fromSequenceNumber = null;

        for (int pageNumber = 0; pageNumber < 20; pageNumber++)
        {
            IReadOnlyList<ExplorerMessage> page = await service.PeekMessagesAsync(
                address,
                bucket,
                pageSize,
                fromSequenceNumber,
                cancellationToken);
            if (page.Count == 0)
            {
                return messages;
            }

            Assert.InRange(page.Count, 1, pageSize);
            messages.AddRange(page);
            fromSequenceNumber = checked(page[^1].SequenceNumber + 1);
        }

        throw new Xunit.Sdk.XunitException("Peek cursor did not reach an empty page within the bounded page limit.");
    }

    private static void AssertSubscriptionMessages(
        IReadOnlyList<ExplorerMessage> messages,
        IReadOnlyDictionary<string, byte[]> expectedBodies,
        int expectedCount)
    {
        Assert.Equal(expectedCount, messages.Count);
        Assert.All(messages, message => AssertFixtureMessage(message, expectedBodies, "topic"));
    }

    private static void AssertFixtureMessage(
        ExplorerMessage message,
        IReadOnlyDictionary<string, byte[]> expectedBodies,
        string expectedSource)
    {
        Assert.True(expectedBodies.TryGetValue(message.MessageId, out byte[]? expectedBody));
        Assert.NotNull(message.RawBody);
        Assert.Equal(expectedBody!, message.RawBody!.ToArray());
        Assert.Equal(expectedBody!.Length, message.BodySizeBytes);
        Assert.Equal(Encoding.UTF8.GetString(expectedBody!), message.Body);
        Assert.Equal(expectedSource, message.ApplicationProperties["source"]);
        Assert.Equal("investigation-peek", message.ApplicationProperties["fixture"]);
    }

    private static void AssertStrictlyIncreasingSequenceNumbers(IReadOnlyList<ExplorerMessage> messages)
    {
        Assert.All(messages, message => Assert.True(message.SequenceNumber > 0));
        for (int index = 1; index < messages.Count; index++)
        {
            Assert.True(messages[index].SequenceNumber > messages[index - 1].SequenceNumber);
        }
    }

    private static async Task<RuntimeCountsObservation> ObserveQueueCountsAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        CancellationToken cancellationToken)
    {
        Response<QueueRuntimeProperties> response = await adminClient.GetQueueRuntimePropertiesAsync(queueName, cancellationToken);
        QueueRuntimeProperties properties = response.Value;
        return new RuntimeCountsObservation(
            properties.ActiveMessageCount,
            properties.DeadLetterMessageCount,
            properties.TotalMessageCount);
    }

    private static async Task<RuntimeCountsObservation> ObserveSubscriptionCountsAsync(
        ServiceBusAdministrationClient adminClient,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        Response<SubscriptionRuntimeProperties> response = await adminClient.GetSubscriptionRuntimePropertiesAsync(
            topicName,
            subscriptionName,
            cancellationToken);
        SubscriptionRuntimeProperties properties = response.Value;
        return new RuntimeCountsObservation(
            properties.ActiveMessageCount,
            properties.DeadLetterMessageCount,
            properties.TotalMessageCount);
    }

    private static void AssertRuntimeCountsAreNonNegative(RuntimeCountsObservation observation)
    {
        Assert.True(observation.ActiveMessageCount >= 0);
        Assert.True(observation.DeadLetterMessageCount >= 0);
        Assert.True(observation.TotalMessageCount >= 0);
    }

    private void WriteRuntimeCounts(string kind, string address, RuntimeCountsObservation observation)
    {
        output.WriteLine(
            $"Observed {kind} runtime counts for {address}: active={observation.ActiveMessageCount}, " +
            $"deadLetter={observation.DeadLetterMessageCount}, " +
            $"total={observation.TotalMessageCount}. Counts are diagnostic observations only.");
    }

    private static string CreateEntityName(string kind, string runId)
    {
        return $"it-peek-{kind}-{runId}";
    }

    private async Task DeleteQueueAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        bool failOnError)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (await adminClient.QueueExistsAsync(queueName, cleanupTimeout.Token))
            {
                await adminClient.DeleteQueueAsync(queueName, cleanupTimeout.Token);
            }
        }
        catch (Exception exception)
        {
            output.WriteLine($"Cleanup failed for generated queue {queueName}: {exception.GetType().Name}: {exception.Message}");
            if (failOnError)
            {
                throw;
            }
        }
    }

    private async Task DeleteTopicAsync(
        ServiceBusAdministrationClient adminClient,
        string topicName,
        bool failOnError)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (await adminClient.TopicExistsAsync(topicName, cleanupTimeout.Token))
            {
                await adminClient.DeleteTopicAsync(topicName, cleanupTimeout.Token);
            }
        }
        catch (Exception exception)
        {
            output.WriteLine($"Cleanup failed for generated topic {topicName}: {exception.GetType().Name}: {exception.Message}");
            if (failOnError)
            {
                throw;
            }
        }
    }

    private sealed record RuntimeCountsObservation(
        long ActiveMessageCount,
        long DeadLetterMessageCount,
        long TotalMessageCount);
}
