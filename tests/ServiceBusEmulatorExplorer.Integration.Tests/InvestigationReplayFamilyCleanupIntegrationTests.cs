using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class InvestigationReplayFamilyCleanupIntegrationTests(ITestOutputHelper output)
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public Task Queue_family_absence_requires_confirmed_deletion_of_original_and_replay_copy() => RunAsync(false);

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public Task Topic_family_remains_present_until_replay_copies_in_both_subscriptions_are_deleted() => RunAsync(true);

    private async Task RunAsync(bool topic)
    {
        string entity = $"it-family-{Guid.NewGuid():N}";
        var source = topic ? new EntityAddress(EntityKind.Subscription, "first", entity) : new EntityAddress(EntityKind.Queue, entity);
        var secondSource = new EntityAddress(EntityKind.Subscription, "second", entity);
        var profile = new ConnectionProfile("Replay family integration", ServiceBusEmulatorEnvironment.RuntimeConnectionString,
            ServiceBusEmulatorEnvironment.AdminConnectionString);
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        bool created = false;
        Exception? failure = null;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            if (topic) await admin.CreateTopicAsync(entity, timeout.Token);
            else await admin.CreateQueueAsync(entity, timeout.Token);
            created = true;
            if (topic)
            {
                await admin.CreateSubscriptionAsync(entity, "first", timeout.Token);
                await admin.CreateSubscriptionAsync(entity, "second", timeout.Token);
            }
            await using var client = new ServiceBusClient(ServiceBusEmulatorEnvironment.RuntimeConnectionString);
            await using var sender = client.CreateSender(entity);
            await using var active = Receiver(client, source, false);
            await using var dlq = Receiver(client, source, true);
            await using var secondActive = topic ? Receiver(client, secondSource, false) : null;
            await using var secondDlq = topic ? Receiver(client, secondSource, true) : null;
            var seed = new ServiceBusMessage(BinaryData.FromBytes(new byte[] { 0, 255, 128, 65 }))
            {
                MessageId = $"original-{Guid.NewGuid():N}", ContentType = "application/octet-stream"
            };
            await sender.SendMessageAsync(seed, timeout.Token);
            // Explicit fixture consumers create DLQ arrivals; Explorer never mutates Active messages.
            await active.DeadLetterMessageAsync(await ReceiveAsync(active, timeout.Token), cancellationToken: timeout.Token);
            if (secondActive is not null)
                await secondActive.CompleteMessageAsync(await ReceiveAsync(secondActive, timeout.Token), timeout.Token);
            var original = Delivery(source, Assert.Single(await dlq.PeekMessagesAsync(10, fromSequenceNumber: 0, cancellationToken: timeout.Token)));
            var reservation = ReplayLineage.Reserve(original, []);
            await using var replay = new ReplayCopySender(profile);
            await replay.SendAsync(ReplayLineage.Destination(source), ReplayLineage.CreateMessage(original, reservation), timeout.Token);
            var firstCopy = await ReceiveAsync(active, timeout.Token);
            AssertLineage(seed, firstCopy, reservation);
            await active.DeadLetterMessageAsync(firstCopy, cancellationToken: timeout.Token);
            if (secondActive is not null)
            {
                var secondCopy = await ReceiveAsync(secondActive, timeout.Token);
                AssertLineage(seed, secondCopy, reservation);
                await secondActive.DeadLetterMessageAsync(secondCopy, cancellationToken: timeout.Token);
            }
            var copy = Delivery(source, Assert.Single(await dlq.PeekMessagesAsync(10, fromSequenceNumber: 0, cancellationToken: timeout.Token),
                message => message.MessageId == reservation.MessageId));
            await using var factory = new DirectServiceBusClientFactory();
            await factory.ConnectAsync(profile, timeout.Token);
            var browser = new InvestigationEntityBrowser(factory);
            var scanner = new ReplayFamilyScanner(new ServiceBusMessageService(factory));
            var deleter = new DlqDeliveryDeleter(address => new DlqDeleteReceiver(profile, address));

            await AssertPresenceAsync(ReplayFamilyPresence.Present, scanner, browser, reservation.Family, timeout.Token);
            await DeleteConfirmedAsync(deleter, original, timeout.Token);
            await AssertPresenceAsync(ReplayFamilyPresence.Present, scanner, browser, reservation.Family, timeout.Token);
            await DeleteConfirmedAsync(deleter, copy, timeout.Token);
            Assert.Empty(await dlq.PeekMessagesAsync(10, fromSequenceNumber: 0, cancellationToken: timeout.Token));
            if (secondDlq is not null)
            {
                // The original subscription is empty, but a sibling still retains this replay family.
                await AssertPresenceAsync(ReplayFamilyPresence.Present, scanner, browser, reservation.Family, timeout.Token);
                var remaining = Delivery(secondSource, Assert.Single(await secondDlq.PeekMessagesAsync(10, fromSequenceNumber: 0, cancellationToken: timeout.Token)));
                Assert.Equal(reservation.MessageId, remaining.Message.MessageId);
                await DeleteConfirmedAsync(deleter, remaining, timeout.Token);
                Assert.Empty(await secondDlq.PeekMessagesAsync(10, fromSequenceNumber: 0, cancellationToken: timeout.Token));
            }
            await AssertPresenceAsync(ReplayFamilyPresence.Absent, scanner, browser, reservation.Family, timeout.Token);
            output.WriteLine($"{(topic ? "Topic fan-out" : "Queue")} family {reservation.Family.FamilyId}: fresh broker discovery and complete DLQ scans proved absence only after all original/replay deliveries were confirmed deleted.");
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    if (topic) await admin.DeleteTopicAsync(entity, cleanup.Token);
                    else await admin.DeleteQueueAsync(entity, cleanup.Token);
                }
                catch (Exception cleanupFailure)
                {
                    output.WriteLine($"Replay family fixture cleanup failed: {cleanupFailure}");
                    if (failure is not null) throw new AggregateException("Family proof and cleanup both failed.", failure, cleanupFailure);
                    throw;
                }
            }
        }
    }

    private static ServiceBusReceiver Receiver(ServiceBusClient client, EntityAddress source, bool deadLetter)
    {
        var options = new ServiceBusReceiverOptions { SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None };
        return source.Kind == EntityKind.Queue ? client.CreateReceiver(source.Name, options) : client.CreateReceiver(source.TopicName!, source.Name, options);
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        Assert.IsType<ServiceBusReceivedMessage>(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), token));

    private static MessageDelivery Delivery(EntityAddress source, ServiceBusReceivedMessage message) =>
        new(new DeliveryIdentity(1, source, MessageBucket.DeadLetter, message.SequenceNumber), MessageProjection.Create(message));

    private static void AssertLineage(ServiceBusMessage original, ServiceBusReceivedMessage copy, ReplayReservation reservation)
    {
        Assert.NotEqual(original.MessageId, copy.MessageId);
        Assert.Equal(reservation.MessageId, copy.MessageId);
        Assert.Equal(original.Body.ToArray(), copy.Body.ToArray());
        Assert.Equal(reservation.Family.FamilyId.ToString("N"), Assert.IsType<string>(copy.ApplicationProperties[ReplayLineage.FamilyProperty]));
        Assert.Equal(reservation.Family.RootFingerprint, Assert.IsType<string>(copy.ApplicationProperties[ReplayLineage.RootProperty]));
        Assert.Equal(original.MessageId, Assert.IsType<string>(copy.ApplicationProperties[ReplayLineage.OriginalIdProperty]));
        Assert.Equal(1L, Assert.IsType<long>(copy.ApplicationProperties[ReplayLineage.AttemptProperty]));
    }

    private static async Task AssertPresenceAsync(ReplayFamilyPresence expected, ReplayFamilyScanner scanner,
        InvestigationEntityBrowser browser, ReplayFamilyState family, CancellationToken token)
    {
        var discovery = await browser.DiscoverAsync(token);
        Assert.True(discovery.IsComplete);
        Assert.Empty(discovery.Issues);
        var result = await scanner.ScanAsync(family, discovery, 1, 10_000, token);
        Assert.Equal(expected, result.Presence);
    }

    private static async Task DeleteConfirmedAsync(DlqDeliveryDeleter deleter, MessageDelivery delivery, CancellationToken token)
    {
        var result = await deleter.DeleteAsync([delivery], token);
        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes).Status);
        Assert.False(result.ScanLimitReached);
        Assert.False(result.CleanupIncomplete);
    }
}
