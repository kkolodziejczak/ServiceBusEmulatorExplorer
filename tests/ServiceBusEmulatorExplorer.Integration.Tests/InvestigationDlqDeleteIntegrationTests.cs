using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationDlqDeleteIntegrationTests(ITestOutputHelper output)
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Captured_delete_removes_late_lower_sequence_target_and_reports_vanished_target_without_deleting_neighbors()
    {
        string queue = $"it-delete-{Guid.NewGuid():N}";
        var source = new EntityAddress(EntityKind.Queue, queue);
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        bool created = false;
        Exception? failure = null;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateQueueAsync(queue, timeout.Token);
            created = true;
            await using var client = new ServiceBusClient(ServiceBusEmulatorEnvironment.RuntimeConnectionString);
            await using var sender = client.CreateSender(queue);
            await using var active = client.CreateReceiver(queue);
            await using var dlq = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            // Sequential sends establish the lower target sequence independently of DLQ arrival order.
            await sender.SendMessageAsync(Seed("late-target", [0, 255, 192, 128, 65]), timeout.Token);
            await sender.SendMessageAsync(Seed("dlq-neighbor", [255, 0, 13, 10, 66]), timeout.Token);
            await sender.SendMessageAsync(Seed("vanished-target", [0, 128, 67]), timeout.Token);
            var lower = await ReceiveAsync(active, timeout.Token);
            var neighbor = await ReceiveAsync(active, timeout.Token);
            var vanished = await ReceiveAsync(active, timeout.Token);
            Assert.Equal("late-target", lower.MessageId);
            Assert.Equal("dlq-neighbor", neighbor.MessageId);
            Assert.Equal("vanished-target", vanished.MessageId);
            Assert.True(lower.SequenceNumber < neighbor.SequenceNumber);
            await active.DeadLetterMessageAsync(neighbor, cancellationToken: timeout.Token);
            await active.DeadLetterMessageAsync(vanished, cancellationToken: timeout.Token);
            Assert.Equal(2, (await PeekAllAsync(dlq, timeout.Token)).Count);
            await active.DeadLetterMessageAsync(lower, cancellationToken: timeout.Token);
            var captured = await PeekAllAsync(dlq, timeout.Token);
            Assert.Equal(3, captured.Count);
            var lowerDelivery = Delivery(source, Assert.Single(captured, message => message.MessageId == "late-target"));
            var vanishedDelivery = Delivery(source, Assert.Single(captured, message => message.MessageId == "vanished-target"));
            var neighborBefore = Assert.Single(captured, message => message.MessageId == "dlq-neighbor");

            // Simulate another authorized consumer completing one captured target before Explorer acts.
            var fixtureLocks = new List<ServiceBusReceivedMessage>();
            for (int index = 0; index < 3; index++) fixtureLocks.Add(await ReceiveAsync(dlq, timeout.Token));
            foreach (var message in fixtureLocks)
            {
                if (message.MessageId == "vanished-target") await dlq.CompleteMessageAsync(message, timeout.Token);
                else await dlq.AbandonMessageAsync(message, cancellationToken: timeout.Token);
            }
            await sender.SendMessageAsync(Seed("active-neighbor", [13, 0, 255, 68]), timeout.Token);
            var activeBefore = Assert.Single(await PeekAllAsync(active, timeout.Token));
            var profile = new ConnectionProfile("DLQ delete integration", ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString);
            var deleter = new DlqDeliveryDeleter(address => new DlqDeleteReceiver(profile, address));

            var result = await deleter.DeleteAsync([vanishedDelivery, lowerDelivery], timeout.Token);

            Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(result.Outcomes, item => item.Identity == vanishedDelivery.Identity).Status);
            Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes, item => item.Identity == lowerDelivery.Identity).Status);
            Assert.False(result.ScanLimitReached);
            Assert.False(result.CleanupIncomplete);
            var neighborAfter = Assert.Single(await PeekAllAsync(dlq, timeout.Token));
            AssertPayloadUnchanged(neighborBefore, neighborAfter);
            // DLQ scans acquire and release locks, so their neighbors' DeliveryCount is deliberately not asserted.
            var activeAfter = Assert.Single(await PeekAllAsync(active, timeout.Token));
            AssertPayloadUnchanged(activeBefore, activeAfter);
            Assert.Equal(activeBefore.DeliveryCount, activeAfter.DeliveryCount);
            output.WriteLine($"Confirmed late DLQ target {lowerDelivery.Identity.SequenceNumber}; vanished target unavailable; DLQ neighbor {neighborAfter.SequenceNumber} and Active neighbor {activeAfter.SequenceNumber} retained.");
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            if (created) await CleanupAsync(admin, queue, failure);
        }
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Subscription_delete_targets_only_selected_dlq_delivery()
    {
        string topic = $"it-delete-topic-{Guid.NewGuid():N}";
        var source = new EntityAddress(EntityKind.Subscription, "first", topic);
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        bool created = false;
        Exception? failure = null;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateTopicAsync(topic, timeout.Token);
            created = true;
            await admin.CreateSubscriptionAsync(topic, "first", timeout.Token);
            await using var client = new ServiceBusClient(ServiceBusEmulatorEnvironment.RuntimeConnectionString);
            await using var sender = client.CreateSender(topic);
            await using var active = client.CreateReceiver(topic, "first");
            await using var dlq = client.CreateReceiver(topic, "first", new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            await sender.SendMessageAsync(Seed("selected", [0, 255, 128, 65]), timeout.Token);
            await sender.SendMessageAsync(Seed("retained", [255, 0, 13, 66]), timeout.Token);
            await active.DeadLetterMessageAsync(await ReceiveAsync(active, timeout.Token), cancellationToken: timeout.Token);
            await active.DeadLetterMessageAsync(await ReceiveAsync(active, timeout.Token), cancellationToken: timeout.Token);
            var before = await PeekAllAsync(dlq, timeout.Token);
            Assert.Equal(2, before.Count);
            var selected = Delivery(source, Assert.Single(before, message => message.MessageId == "selected"));
            var retained = Assert.Single(before, message => message.MessageId == "retained");
            var profile = new ConnectionProfile("Subscription delete integration", ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString);
            var deleter = new DlqDeliveryDeleter(address => new DlqDeleteReceiver(profile, address));

            var result = await deleter.DeleteAsync([selected], timeout.Token);

            Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes).Status);
            Assert.False(result.ScanLimitReached);
            Assert.False(result.CleanupIncomplete);
            AssertPayloadUnchanged(retained, Assert.Single(await PeekAllAsync(dlq, timeout.Token)));
            Assert.Empty(await PeekAllAsync(active, timeout.Token));
            output.WriteLine($"Subscription selected DLQ delivery {selected.Identity.SequenceNumber} deleted; neighboring delivery retained.");
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await admin.DeleteTopicAsync(topic, cleanup.Token); }
                catch (Exception cleanupFailure)
                {
                    output.WriteLine($"Subscription delete fixture cleanup failed: {cleanupFailure}");
                    if (failure is not null) throw new AggregateException("Subscription delete proof and cleanup both failed.", failure, cleanupFailure);
                    throw;
                }
            }
        }
    }

    private static ServiceBusMessage Seed(string id, byte[] bytes) => new(BinaryData.FromBytes(bytes))
    {
        MessageId = id, ContentType = "application/octet-stream", CorrelationId = "delete-fixture"
    };

    private static async Task<ServiceBusReceivedMessage> ReceiveAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        Assert.IsType<ServiceBusReceivedMessage>(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), token));

    private static MessageDelivery Delivery(EntityAddress source, ServiceBusReceivedMessage message) =>
        new(new DeliveryIdentity(1, source, MessageBucket.DeadLetter, message.SequenceNumber), MessageProjection.Create(message));

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
        Assert.Fail("The isolated delete fixture did not exhaust within four peek pages.");
        return messages;
    }

    private static void AssertPayloadUnchanged(ServiceBusReceivedMessage before, ServiceBusReceivedMessage after)
    {
        Assert.Equal(before.MessageId, after.MessageId);
        Assert.Equal(before.SequenceNumber, after.SequenceNumber);
        Assert.Equal(before.Body.ToArray(), after.Body.ToArray());
        Assert.Equal(before.ContentType, after.ContentType);
        Assert.Equal(before.CorrelationId, after.CorrelationId);
    }

    private async Task CleanupAsync(ServiceBusAdministrationClient admin, string queue, Exception? testFailure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await admin.DeleteQueueAsync(queue, timeout.Token); }
        catch (Exception cleanupFailure)
        {
            output.WriteLine($"Delete fixture cleanup failed: {cleanupFailure}");
            if (testFailure is not null) throw new AggregateException("Delete proof and fixture cleanup both failed.", testFailure, cleanupFailure);
            throw;
        }
    }
}
