using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationReplayIntegrationTests(ITestOutputHelper output)
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Queue_replay_preserves_original_and_binary_metadata_and_advances_received_copy_lineage()
    {
        string queue = $"it-replay-{Guid.NewGuid():N}";
        var source = new EntityAddress(EntityKind.Queue, queue);
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        Exception? failure = null;
        bool created = false;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateQueueAsync(queue, timeout.Token);
            created = true;
            await using var client = new ServiceBusClient(ServiceBusEmulatorEnvironment.RuntimeConnectionString);
            await using var fixtureSender = client.CreateSender(queue);
            await using var active = client.CreateReceiver(queue);
            await using var dlq = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            var seed = SeedMessage();
            await fixtureSender.SendMessageAsync(seed, timeout.Token);
            var received = await ReceiveAsync(active, timeout.Token);
            await active.DeadLetterMessageAsync(received, cancellationToken: timeout.Token);
            var original = Assert.Single(await PeekAllAsync(dlq, timeout.Token));
            var delivery = Delivery(source, original);
            var first = ReplayLineage.Reserve(delivery, []);
            await using var replay = new ReplayCopySender(Profile());
            await replay.SendAsync(ReplayLineage.Destination(source), ReplayLineage.CreateMessage(delivery, first), timeout.Token);

            var firstCopy = await ReceiveAsync(active, timeout.Token);
            AssertCopy(seed, firstCopy, first);
            AssertUnchanged(original, Assert.Single(await PeekAllAsync(dlq, timeout.Token)));
            // This is an explicit fixture consumer failure, never an Explorer Active mutation.
            await active.DeadLetterMessageAsync(firstCopy, cancellationToken: timeout.Token);
            var firstDlq = Assert.Single(await PeekAllAsync(dlq, timeout.Token), message => message.MessageId == first.MessageId);
            var secondDelivery = Delivery(source, firstDlq);
            var second = ReplayLineage.Reserve(secondDelivery, [first.Family]);
            Assert.Equal(2L, second.Family.LastAttempt);
            Assert.Equal(first.Family.FamilyId, second.Family.FamilyId);
            Assert.NotEqual(first.MessageId, second.MessageId);
            await replay.SendAsync(ReplayLineage.Destination(source), ReplayLineage.CreateMessage(secondDelivery, second), timeout.Token);
            var secondCopy = await ReceiveAsync(active, timeout.Token);
            AssertCopy(seed, secondCopy, second);
            await active.CompleteMessageAsync(secondCopy, timeout.Token);
            var remaining = await PeekAllAsync(dlq, timeout.Token);
            Assert.Equal(2, remaining.Count);
            AssertUnchanged(original, Assert.Single(remaining, message => message.MessageId == original.MessageId));
            AssertUnchanged(firstDlq, Assert.Single(remaining, message => message.MessageId == first.MessageId));
            output.WriteLine($"Queue replay attempts 1 and 2 confirmed; original and replay DLQ tuples unchanged: {Observation(original)}, {Observation(firstDlq)}.");
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            if (created) await CleanupAsync(token => admin.DeleteQueueAsync(queue, token), failure);
        }
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Subscription_replay_publishes_parent_topic_to_both_subscriptions_without_touching_original()
    {
        string topic = $"it-replay-topic-{Guid.NewGuid():N}";
        var source = new EntityAddress(EntityKind.Subscription, "first", topic);
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        Exception? failure = null;
        bool created = false;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateTopicAsync(topic, timeout.Token);
            created = true;
            await admin.CreateSubscriptionAsync(topic, "first", timeout.Token);
            await admin.CreateSubscriptionAsync(topic, "second", timeout.Token);
            await using var client = new ServiceBusClient(ServiceBusEmulatorEnvironment.RuntimeConnectionString);
            await using var fixtureSender = client.CreateSender(topic);
            await using var firstActive = client.CreateReceiver(topic, "first");
            await using var secondActive = client.CreateReceiver(topic, "second");
            await using var dlq = client.CreateReceiver(topic, "first", new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            var seed = SeedMessage();
            await fixtureSender.SendMessageAsync(seed, timeout.Token);
            var received = await ReceiveAsync(firstActive, timeout.Token);
            await firstActive.DeadLetterMessageAsync(received, cancellationToken: timeout.Token);
            await secondActive.CompleteMessageAsync(await ReceiveAsync(secondActive, timeout.Token), timeout.Token);
            var original = Assert.Single(await PeekAllAsync(dlq, timeout.Token));
            var delivery = Delivery(source, original);
            var reservation = ReplayLineage.Reserve(delivery, []);
            var destination = ReplayLineage.Destination(source);
            Assert.Equal(new EntityAddress(EntityKind.Topic, topic), destination);
            await using var replay = new ReplayCopySender(Profile());
            await replay.SendAsync(destination, ReplayLineage.CreateMessage(delivery, reservation), timeout.Token);
            var firstCopy = await ReceiveAsync(firstActive, timeout.Token);
            var secondCopy = await ReceiveAsync(secondActive, timeout.Token);
            AssertCopy(seed, firstCopy, reservation);
            AssertCopy(seed, secondCopy, reservation);
            await firstActive.CompleteMessageAsync(firstCopy, timeout.Token);
            await secondActive.CompleteMessageAsync(secondCopy, timeout.Token);
            AssertUnchanged(original, Assert.Single(await PeekAllAsync(dlq, timeout.Token)));
            Assert.Empty(await PeekAllAsync(firstActive, timeout.Token));
            Assert.Empty(await PeekAllAsync(secondActive, timeout.Token));
            output.WriteLine($"Parent topic replay reached both subscriptions with ID {reservation.MessageId}; original DLQ tuple unchanged: {Observation(original)}.");
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            if (created) await CleanupAsync(token => admin.DeleteTopicAsync(topic, token), failure);
        }
    }

    private static ConnectionProfile Profile() => new("Replay integration", ServiceBusEmulatorEnvironment.RuntimeConnectionString,
        ServiceBusEmulatorEnvironment.AdminConnectionString);

    private static ServiceBusMessage SeedMessage()
    {
        var message = new ServiceBusMessage(BinaryData.FromBytes(new byte[] { 0, 255, 192, 128, 13, 10, 65 }))
        {
            MessageId = $"binary-original-{Guid.NewGuid():N}", ContentType = "application/octet-stream",
            CorrelationId = "replay-case", Subject = "binary fixture", ReplyTo = "reply-target",
            ReplyToSessionId = "reply-session", To = "logical-target", TimeToLive = TimeSpan.FromHours(1)
        };
        message.ApplicationProperties["fixture-text"] = "preserved";
        message.ApplicationProperties["fixture-number"] = 42L;
        message.ApplicationProperties["fixture-flag"] = true;
        return message;
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        Assert.IsType<ServiceBusReceivedMessage>(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), token));

    private static MessageDelivery Delivery(EntityAddress source, ServiceBusReceivedMessage message) =>
        new(new DeliveryIdentity(1, source, MessageBucket.DeadLetter, message.SequenceNumber), MessageProjection.Create(message));

    private static void AssertCopy(ServiceBusMessage original, ServiceBusReceivedMessage copy, ReplayReservation reservation)
    {
        Assert.Equal(original.Body.ToArray(), copy.Body.ToArray());
        Assert.Equal(reservation.MessageId, copy.MessageId);
        Assert.NotEqual(original.MessageId, copy.MessageId);
        Assert.Equal(original.ContentType, copy.ContentType);
        Assert.Equal(original.CorrelationId, copy.CorrelationId);
        Assert.Equal(original.Subject, copy.Subject);
        Assert.Equal(original.ReplyTo, copy.ReplyTo);
        Assert.Equal(original.ReplyToSessionId, copy.ReplyToSessionId);
        Assert.Equal(original.To, copy.To);
        Assert.Equal(original.TimeToLive, copy.TimeToLive);
        foreach (var property in original.ApplicationProperties)
            Assert.Equal(property.Value, copy.ApplicationProperties[property.Key]);
        Assert.Equal(reservation.Family.FamilyId.ToString("N"), copy.ApplicationProperties[ReplayLineage.FamilyProperty]);
        Assert.Equal(reservation.Family.RootFingerprint, copy.ApplicationProperties[ReplayLineage.RootProperty]);
        Assert.Equal(original.MessageId, copy.ApplicationProperties[ReplayLineage.OriginalIdProperty]);
        Assert.Equal(reservation.Family.LastAttempt, Assert.IsType<long>(copy.ApplicationProperties[ReplayLineage.AttemptProperty]));
    }

    private static void AssertUnchanged(ServiceBusReceivedMessage before, ServiceBusReceivedMessage after)
    {
        Assert.Equal(Observation(before), Observation(after));
        Assert.Equal(before.Body.ToArray(), after.Body.ToArray());
    }

    private static (string MessageId, long SequenceNumber, int DeliveryCount) Observation(ServiceBusReceivedMessage message) =>
        (message.MessageId, message.SequenceNumber, message.DeliveryCount);

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAllAsync(ServiceBusReceiver receiver, CancellationToken token)
    {
        var messages = new List<ServiceBusReceivedMessage>();
        long next = 0;
        for (int page = 0; page < 4; page++)
        {
            var batch = await receiver.PeekMessagesAsync(10, next, token);
            if (batch.Count == 0) return messages;
            Assert.All(batch, message => Assert.True(message.SequenceNumber >= next));
            messages.AddRange(batch);
            next = checked(batch.Max(message => message.SequenceNumber) + 1);
        }
        Assert.Fail("The isolated replay fixture did not exhaust within four peek pages.");
        return messages;
    }

    private async Task CleanupAsync(Func<CancellationToken, Task> delete, Exception? testFailure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await delete(timeout.Token); }
        catch (Exception cleanupFailure)
        {
            output.WriteLine($"Replay fixture cleanup failed: {cleanupFailure}");
            if (testFailure is not null) throw new AggregateException("Replay proof and fixture cleanup both failed.", testFailure, cleanupFailure);
            throw;
        }
    }
}
