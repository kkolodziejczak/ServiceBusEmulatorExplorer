using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationActiveDeletionSafetyIntegrationTests
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Releasing_an_unselected_message_can_dead_letter_it_before_a_later_target_is_received()
    {
        string queue = "investigation-delete-safety-" + Guid.NewGuid().ToString("N");
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        bool created = false;
        Exception? failure = null;
        try
        {
            await admin.CreateQueueAsync(new CreateQueueOptions(queue) { MaxDeliveryCount = 1 }, timeout.Token);
            created = true;
            await using var client = new ServiceBusClient(ServiceBusEmulatorEnvironment.RuntimeConnectionString);
            await using var sender = client.CreateSender(queue);
            await sender.SendMessageAsync(new ServiceBusMessage("unselected") { MessageId = "unselected" }, timeout.Token);
            await sender.SendMessageAsync(new ServiceBusMessage("selected") { MessageId = "selected" }, timeout.Token);
            await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock, PrefetchCount = 0
            });
            await using var deadLetter = client.CreateReceiver(queue, new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter
            });
            var before = await PeekAllAsync(receiver, timeout.Token);
            Assert.Equal(new[] { "unselected", "selected" }, before.Select(message => message.MessageId));
            long selectedSequence = before.Single(message => message.MessageId == "selected").SequenceNumber;
            var unrelated = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), timeout.Token);
            Assert.NotNull(unrelated);
            Assert.Equal("unselected", unrelated.MessageId);
            Assert.NotEqual(selectedSequence, unrelated.SequenceNumber);

            // This is the broker primitive an arbitrary-target receive/abandon scan would use.
            await receiver.AbandonMessageAsync(unrelated, cancellationToken: timeout.Token);

            var moved = Assert.Single(await PeekAllAsync(deadLetter, timeout.Token));
            Assert.Equal("unselected", moved.MessageId);
            Assert.Equal(unrelated.SequenceNumber, moved.SequenceNumber);
            Assert.Equal("MaxDeliveryCountExceeded", moved.DeadLetterReason);
            var retained = Assert.Single(await PeekAllAsync(receiver, timeout.Token));
            Assert.Equal("selected", retained.MessageId);
            Assert.Equal(selectedSequence, retained.SequenceNumber);
            Assert.Equal(before.Single(message => message.MessageId == "selected").DeliveryCount, retained.DeliveryCount);
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await admin.DeleteQueueAsync(queue, cleanup.Token); }
                catch when (failure is not null) { /* Preserve the original failed proof. */ }
            }
        }
    }

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAllAsync(
        ServiceBusReceiver receiver, CancellationToken cancellationToken)
    {
        var messages = new List<ServiceBusReceivedMessage>();
        long nextSequence = 0;
        for (int page = 0; page < 4; page++)
        {
            var batch = await receiver.PeekMessagesAsync(10, nextSequence, cancellationToken);
            if (batch.Count == 0) return messages;
            Assert.All(batch, message => Assert.True(message.SequenceNumber >= nextSequence));
            messages.AddRange(batch);
            nextSequence = checked(batch.Max(message => message.SequenceNumber) + 1);
        }
        Assert.Fail("The isolated two-message fixture did not exhaust within four peek pages.");
        return messages;
    }
}
