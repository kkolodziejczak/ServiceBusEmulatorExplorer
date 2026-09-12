using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationDlqArrivalIntegrationTests(ITestOutputHelper output)
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Late_dead_letter_arrival_is_missed_by_forward_sequence_watermark_but_visible_to_full_peek()
    {
        string runId = Guid.NewGuid().ToString("N");
        string queueName = $"it-dlq-arrival-{runId}";
        string olderId = $"older-{runId}";
        string newerId = $"newer-{runId}";
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        Exception? testFailure = null;

        try
        {
            await admin.CreateQueueAsync(new CreateQueueOptions(queueName)
            {
                LockDuration = TimeSpan.FromMinutes(2)
            }, timeout.Token);
            await using var factory = new DirectServiceBusClientFactory();
            await factory.ConnectAsync(new ConnectionProfile("Watch proof", ServiceBusEmulatorEnvironment.RuntimeConnectionString,
                ServiceBusEmulatorEnvironment.AdminConnectionString), timeout.Token);
            var client = factory.RuntimeClient;
            await using ServiceBusSender sender = client.CreateSender(queueName);
            await sender.SendMessageAsync(new ServiceBusMessage("older") { MessageId = olderId }, timeout.Token);
            await sender.SendMessageAsync(new ServiceBusMessage("newer") { MessageId = newerId }, timeout.Token);

            await using ServiceBusReceiver active = client.CreateReceiver(queueName, new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
            ServiceBusReceivedMessage older = Assert.IsType<ServiceBusReceivedMessage>(
                await active.ReceiveMessageAsync(TimeSpan.FromSeconds(10), timeout.Token));
            ServiceBusReceivedMessage newer = Assert.IsType<ServiceBusReceivedMessage>(
                await active.ReceiveMessageAsync(TimeSpan.FromSeconds(10), timeout.Token));
            Assert.Equal(olderId, older.MessageId);
            Assert.Equal(newerId, newer.MessageId);
            Assert.True(older.SequenceNumber < newer.SequenceNumber);
            WriteObservation("received older", older);
            WriteObservation("received newer", newer);

            await active.DeadLetterMessageAsync(newer, cancellationToken: timeout.Token);
            await using ServiceBusReceiver dlq = client.CreateReceiver(queueName, new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
            ServiceBusReceivedMessage firstArrival = Assert.Single(await PeekAllAsync(dlq, timeout.Token));
            Assert.Equal(newerId, firstArrival.MessageId);
            Assert.Equal(newer.SequenceNumber, firstArrival.SequenceNumber);
            long previousMaximum = firstArrival.SequenceNumber;
            WriteObservation("first DLQ arrival", firstArrival);
            var watch = new DeliveryWatch(new ServiceBusMessageService(factory));
            watch.SetTargets(1, [new WatchTarget(new EntityAddress(EntityKind.Queue, queueName), MessageBucket.DeadLetter)]);
            var baseline = await watch.PollAsync(10, timeout.Token);
            Assert.Empty(baseline.Arrivals);
            Assert.Empty(baseline.Failures);
            Assert.Equal(0, baseline.BaselinesPending);

            await active.DeadLetterMessageAsync(older, cancellationToken: timeout.Token);
            IReadOnlyList<ServiceBusReceivedMessage> allArrivals = await PeekAllAsync(dlq, timeout.Token);
            Assert.Equal(2, allArrivals.Count);
            ServiceBusReceivedMessage lateArrival = Assert.Single(allArrivals, message => message.MessageId == olderId);
            Assert.Equal(older.SequenceNumber, lateArrival.SequenceNumber);
            Assert.True(lateArrival.SequenceNumber < previousMaximum);
            foreach (ServiceBusReceivedMessage message in allArrivals)
                WriteObservation("full DLQ peek", message);

            IReadOnlyList<ServiceBusReceivedMessage> forwardOnly = await dlq.PeekMessagesAsync(
                10, fromSequenceNumber: checked(previousMaximum + 1), cancellationToken: timeout.Token);
            Assert.Empty(forwardOnly);
            output.WriteLine($"Forward peek from sequence {previousMaximum + 1}: no deliveries.");
            var watched = await watch.PollAsync(10, timeout.Token);
            Assert.Empty(watched.Failures);
            var detected = Assert.Single(watched.Arrivals);
            Assert.Equal(olderId, detected.Message.MessageId);
            Assert.Equal(older.SequenceNumber, detected.Identity.SequenceNumber);
            Assert.Empty((await watch.PollAsync(10, timeout.Token)).Arrivals);
            watch.SetTargets(2, [new WatchTarget(new EntityAddress(EntityKind.Queue, queueName), MessageBucket.DeadLetter)]);
            Assert.Empty((await watch.PollAsync(10, timeout.Token)).Arrivals);

            IReadOnlyList<ServiceBusReceivedMessage> repeated = await PeekAllAsync(dlq, timeout.Token);
            Assert.Equal(Observations(allArrivals), Observations(repeated));
            foreach (ServiceBusReceivedMessage message in repeated)
                WriteObservation("repeated DLQ peek", message);
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await DeleteQueueAsync(admin, queueName, testFailure is null);
        }
    }

    private void WriteObservation(string stage, ServiceBusReceivedMessage message) =>
        output.WriteLine($"{stage}: MessageId={message.MessageId}; SequenceNumber={message.SequenceNumber}; DeliveryCount={message.DeliveryCount}");

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAllAsync(
        ServiceBusReceiver receiver,
        CancellationToken cancellationToken)
    {
        // Explicit zero resets the SDK peek cursor; a short page is not proof of exhaustion.
        var messages = new List<ServiceBusReceivedMessage>();
        long fromSequence = 0;
        for (int page = 0; page < 4; page++)
        {
            IReadOnlyList<ServiceBusReceivedMessage> next = await receiver.PeekMessagesAsync(
                10, fromSequenceNumber: fromSequence, cancellationToken: cancellationToken);
            if (next.Count == 0)
                return messages;
            Assert.All(next, message => Assert.True(message.SequenceNumber >= fromSequence));
            messages.AddRange(next);
            fromSequence = checked(next.Max(message => message.SequenceNumber) + 1);
        }

        Assert.Fail("The unique two-message fixture did not exhaust within four peek pages.");
        return messages;
    }

    private static IEnumerable<(string MessageId, long SequenceNumber, int DeliveryCount)> Observations(
        IEnumerable<ServiceBusReceivedMessage> messages) =>
        messages.OrderBy(message => message.SequenceNumber)
            .Select(message => (message.MessageId, message.SequenceNumber, message.DeliveryCount));

    private static async Task DeleteQueueAsync(ServiceBusAdministrationClient admin, string queueName, bool failOnError)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (await admin.QueueExistsAsync(queueName, cleanupTimeout.Token))
                await admin.DeleteQueueAsync(queueName, cleanupTimeout.Token);
        }
        catch when (!failOnError)
        {
        }
    }
}
