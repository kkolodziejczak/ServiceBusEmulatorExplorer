using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.Messaging;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class DailyOperationsAuditTests(ITestOutputHelper output)
{
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(10);

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Queue_replay_copy_preserves_body_properties_and_original_until_delete()
    {
        string queue = Name("audit-replay");
        await RunWithQueueAsync(queue, async (admin, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "{\"orderId\":\"A-1\"}",
                ContentType: "application/json", CorrelationId: "audit-1", Subject: "OrderCreated",
                ApplicationProperties: new Dictionary<string, object?> { ["tenant"] = "demo", ["attempt"] = 1L }), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            ExplorerMessage original = Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));

            MessageDelivery delivery = Delivery(address, original);
            ReplayReservation reservation = await ReplayCurrentAsync(delivery, Profile(), token);

            ExplorerMessage copy = Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token));
            Assert.Equal("{\"orderId\":\"A-1\"}", copy.Body);
            Assert.Equal("application/json", copy.ContentType);
            Assert.Equal("audit-1", copy.CorrelationId);
            Assert.Equal("demo", copy.ApplicationProperties["tenant"]);
            Assert.Equal(reservation.MessageId, copy.MessageId);
            Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));

            DlqDeleteResult deletion = await DeleteCurrentAsync(Profile(), token, delivery);
            Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(deletion.Outcomes).Status);
            Assert.Empty(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Repeated_replay_attempts_use_distinct_message_ids_and_keep_source()
    {
        string queue = Name("audit-repeat");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "repeat-me"), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            ExplorerMessage original = Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
            MessageDelivery delivery = Delivery(address, original);
            ReplayReservation first = await ReplayCurrentAsync(delivery, Profile(), token);
            ReplayReservation second = await ReplayCurrentAsync(delivery, Profile(), token, first.Family);

            Assert.NotEqual(first.MessageId, second.MessageId);
            Assert.Equal(2, (await PeekAsync(messages, address, MessageBucket.Active, token)).Count);
            Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Edited_replay_preserves_explicit_valid_json_and_changes_only_requested_body()
    {
        string queue = Name("audit-edit");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "{\"status\":\"failed\"}",
                ContentType: "application/json", CorrelationId: "corr-edit", Subject: "PaymentFailed",
                ApplicationProperties: new Dictionary<string, object?> { ["case"] = "edit" }), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            ExplorerMessage original = Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
            await ReplayCurrentAsync(Delivery(address, original), Profile(), token, editedBody: "{\"status\":\"recovered\"}");
            ExplorerMessage copy = Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token));
            Assert.Equal("{\"status\":\"recovered\"}", copy.Body);
            Assert.Equal("application/json", copy.ContentType);
            Assert.Equal("corr-edit", copy.CorrelationId);
            Assert.Equal("edit", copy.ApplicationProperties["case"]);
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Topic_replay_fans_out_to_every_subscription_and_keeps_source_dlq()
    {
        string topic = Name("audit-fanout");
        await RunWithTopicAsync(topic, async (_, factory, source, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(new EntityAddress(EntityKind.Topic, topic), "topic-body",
                Subject: "OrderDispatched"), token);
            await using ServiceBusReceiver first = factory.RuntimeClient.CreateReceiver(topic, "first");
            await using ServiceBusReceiver second = factory.RuntimeClient.CreateReceiver(topic, "second");
            await first.DeadLetterMessageAsync(await ReceiveAsync(first, token), cancellationToken: token);
            await second.CompleteMessageAsync(await ReceiveAsync(second, token), token);
            ExplorerMessage original = Assert.Single(await PeekAsync(messages, source, MessageBucket.DeadLetter, token));

            ReplayReservation reservation = ReplayLineage.Reserve(Delivery(source, original), []);
            await using (var sender = new ReplayCopySender(Profile()))
                await sender.SendAsync(ReplayLineage.Destination(source), ReplayLineage.CreateMessage(Delivery(source, original), reservation), token);

            Assert.Equal("topic-body", (await ReceiveAsync(first, token)).Body.ToString());
            Assert.Equal("topic-body", (await ReceiveAsync(second, token)).Body.ToString());
            Assert.Single(await PeekAsync(messages, source, MessageBucket.DeadLetter, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Selective_dlq_delete_removes_only_selected_delivery_and_keeps_active_messages()
    {
        string queue = Name("audit-delete");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "one"), token);
            await messages.SendMessageAsync(new SendMessageCommand(address, "two"), token);
            await messages.SendMessageAsync(new SendMessageCommand(address, "active"), token);
            await using ServiceBusReceiver receiver = factory.RuntimeClient.CreateReceiver(queue);
            await receiver.DeadLetterMessageAsync(await ReceiveAsync(receiver, token), cancellationToken: token);
            await receiver.DeadLetterMessageAsync(await ReceiveAsync(receiver, token), cancellationToken: token);
            IReadOnlyList<ExplorerMessage> deadLetters = await PeekAsync(messages, address, MessageBucket.DeadLetter, token);
            ExplorerMessage selected = deadLetters[0];
            MessageDelivery selectedDelivery = Delivery(address, selected);

            DlqDeleteResult deletion = await DeleteCurrentAsync(Profile(), token, selectedDelivery);
            Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(deletion.Outcomes).Status);
            Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
            Assert.Equal("active", Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token)).Body);
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Stale_dlq_target_reports_failure_without_deleting_a_new_neighbor()
    {
        string queue = Name("audit-stale");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "stale"), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            ExplorerMessage target = Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
            await using (ServiceBusReceiver receiver = factory.RuntimeClient.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }))
                await receiver.CompleteMessageAsync(await ReceiveAsync(receiver, token), token);
            await messages.SendMessageAsync(new SendMessageCommand(address, "new-active"), token);

            DlqDeleteResult deletion = await DeleteCurrentAsync(Profile(), token, Delivery(address, target));
            Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(deletion.Outcomes).Status);
            Assert.Equal("new-active", Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token)).Body);
            Assert.Empty(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Active_message_replay_is_rejected_before_broker_mutation()
    {
        string queue = Name("audit-replay-reject");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "protected"), token);
            ExplorerMessage active = Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token));
            await Assert.ThrowsAsync<ArgumentException>(() => ReplayCurrentAsync(
                new MessageDelivery(new DeliveryIdentity(1, address, MessageBucket.Active, active.SequenceNumber), active),
                Profile(), token));
            Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Empty_dlq_delete_request_is_a_safe_no_op_without_mutation()
    {
        string queue = Name("audit-delete-reject");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "protected"), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            DlqDeleteResult result = await new DlqDeliveryDeleter(addressValue => new DlqDeleteReceiver(Profile(), addressValue))
                .DeleteAsync([], token);
            Assert.Empty(result.Outcomes);
            Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Canceled_replay_scan_leaves_the_original_dlq_delivery_available()
    {
        string queue = Name("audit-replay-cancel");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "cancel-me"), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            ExplorerMessage original = Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReplayCurrentAsync(
                Delivery(address, original), Profile(), canceled.Token));
            Assert.Single(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
            Assert.Empty(await PeekAsync(messages, address, MessageBucket.Active, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Partial_dlq_delete_reports_stale_target_and_deletes_only_confirmed_target()
    {
        string queue = Name("audit-delete-partial");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "valid"), token);
            await messages.SendMessageAsync(new SendMessageCommand(address, "stale"), token);
            await using ServiceBusReceiver receiver = factory.RuntimeClient.CreateReceiver(queue);
            await receiver.DeadLetterMessageAsync(await ReceiveAsync(receiver, token), cancellationToken: token);
            await receiver.DeadLetterMessageAsync(await ReceiveAsync(receiver, token), cancellationToken: token);
            IReadOnlyList<ExplorerMessage> before = await PeekAsync(messages, address, MessageBucket.DeadLetter, token);
            ExplorerMessage valid = before[0];
            ExplorerMessage stale = before[1];
            await using ServiceBusReceiver dlq = factory.RuntimeClient.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            IReadOnlyList<ServiceBusReceivedMessage> locks = await dlq.ReceiveMessagesAsync(2, ReceiveWait, token);
            foreach (ServiceBusReceivedMessage message in locks)
            {
                if (message.SequenceNumber == stale.SequenceNumber) await dlq.CompleteMessageAsync(message, token);
                else await dlq.AbandonMessageAsync(message, cancellationToken: token);
            }

            DlqDeleteResult result = await DeleteCurrentAsync(Profile(), token, Delivery(address, valid), Delivery(address, stale));
            Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(result.Outcomes, outcome => outcome.Identity.SequenceNumber == stale.SequenceNumber).Status);
            Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes, outcome => outcome.Identity.SequenceNumber == valid.SequenceNumber).Status);
            Assert.Empty(await PeekAsync(messages, address, MessageBucket.DeadLetter, token));
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Watch_baselines_existing_active_messages_and_reports_one_new_arrival()
    {
        string queue = Name("audit-watch-active");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "old"), token);
            var watch = new DeliveryWatch(messages);
            watch.SetTargets(1, [new WatchTarget(address, MessageBucket.Active)]);
            Assert.Empty((await watch.PollAsync(20, token)).Arrivals);
            await messages.SendMessageAsync(new SendMessageCommand(address, "new"), token);
            WatchPollResult arrival = await watch.PollAsync(20, token);
            Assert.Equal("new", Assert.Single(arrival.Arrivals).Message.Body);
            Assert.Empty((await watch.PollAsync(20, token)).Arrivals);
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Watch_reports_a_new_dead_letter_arrival_separately_from_active_baseline()
    {
        string queue = Name("audit-watch-dlq");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            var watch = new DeliveryWatch(messages);
            watch.SetTargets(1, [new WatchTarget(address, MessageBucket.DeadLetter)]);
            Assert.Empty((await watch.PollAsync(20, token)).Arrivals);
            await messages.SendMessageAsync(new SendMessageCommand(address, "poison"), token);
            await MoveOneToDlqAsync(factory.RuntimeClient, queue, token);
            WatchPollResult arrival = await watch.PollAsync(20, token);
            Assert.Equal("poison", Assert.Single(arrival.Arrivals).Message.Body);
            Assert.Equal(MessageBucket.DeadLetter, arrival.Arrivals[0].Identity.Bucket);
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Expired_message_is_not_receivable_even_when_diagnostic_peek_retains_it()
    {
        string queue = Name("audit-ttl");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await using ServiceBusSender sender = factory.RuntimeClient.CreateSender(queue);
            await sender.SendMessageAsync(new ServiceBusMessage("expires") { TimeToLive = TimeSpan.FromSeconds(3) }, token);
            var initial = Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token));
            Assert.NotNull(initial.ExpiresAt);
            await Task.Delay(TimeSpan.FromSeconds(4), token);
            await using var receiver = factory.RuntimeClient.CreateReceiver(queue);
            Assert.Null(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), token));
            // Expired entries may remain in diagnostic Peek until lazy garbage collection.
            // https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-expiration
            foreach (var retained in await PeekAsync(messages, address, MessageBucket.Active, token))
                Assert.True(retained.ExpiresAt <= DateTimeOffset.UtcNow);
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Scheduled_message_can_be_peeked_before_due_and_received_after_due()
    {
        string queue = Name("audit-scheduled");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            await using ServiceBusSender sender = factory.RuntimeClient.CreateSender(queue);
            DateTimeOffset due = DateTimeOffset.UtcNow.AddSeconds(5);
            await sender.ScheduleMessageAsync(new ServiceBusMessage("scheduled") { MessageId = "scheduled-audit" }, due, token);
            var messages = new ServiceBusMessageService(factory);
            // Peek is allowed to expose a scheduled message before its due time. The
            // product Scheduled view must therefore treat broker peek output as an
            // observation, while receive availability remains broker-controlled.
            ExplorerMessage observed = Assert.Single(await PeekAsync(messages, address, MessageBucket.Active, token));
            Assert.Equal("scheduled", observed.Body);
            Assert.Equal("scheduled-audit", observed.MessageId);
            await using var receiver = factory.RuntimeClient.CreateReceiver(queue);
            Assert.Null(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), token));
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            Assert.Equal("scheduled", (await ReceiveAsync(receiver, token)).Body.ToString());
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Scheduled_delivery_inspection_preserves_the_broker_scheduled_enqueue_time()
    {
        string queue = Name("audit-scheduled-metadata");
        await RunWithQueueAsync(queue, async (_, factory, address, token) =>
        {
            await using var sender = factory.RuntimeClient.CreateSender(queue);
            DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(2);
            await sender.ScheduleMessageAsync(new ServiceBusMessage("future-order"), due, token);
            await using var receiver = factory.RuntimeClient.CreateReceiver(queue);
            var broker = Assert.IsType<ServiceBusReceivedMessage>(await receiver.PeekMessageAsync(cancellationToken: token));
            Assert.True(broker.ScheduledEnqueueTime > DateTimeOffset.UtcNow);
            var projected = Assert.Single(await PeekAsync(new ServiceBusMessageService(factory), address, MessageBucket.Active, token));
            // A daily operator inspecting a scheduled delivery needs the broker's due timestamp.
            Assert.True(projected.SystemProperties.Values.OfType<DateTimeOffset>()
                .Any(value => value == broker.ScheduledEnqueueTime),
                "The broker exposes ScheduledEnqueueTime but inspection projection loses it.");
        });
    }

    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    [Trait("Audit", "Daily")]
    public async Task Runtime_counts_match_the_messages_visible_to_the_user()
    {
        string queue = Name("audit-counts");
        await RunWithQueueAsync(queue, async (admin, factory, address, token) =>
        {
            var messages = new ServiceBusMessageService(factory);
            await messages.SendMessageAsync(new SendMessageCommand(address, "count-1"), token);
            await messages.SendMessageAsync(new SendMessageCommand(address, "count-2"), token);
            IReadOnlyList<ExplorerMessage> visible = await PeekAsync(messages, address, MessageBucket.Active, token);
            QueueRuntimeProperties counts = (await admin.GetQueueRuntimePropertiesAsync(queue, token)).Value;
            output.WriteLine($"Queue {queue}: visible={visible.Count}; runtime active={counts.ActiveMessageCount}; dlq={counts.DeadLetterMessageCount}.");
            Assert.Equal(visible.Count, counts.ActiveMessageCount);
        });
    }

    private static string Name(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task RunWithQueueAsync(string queue, Func<ServiceBusAdministrationClient, DirectServiceBusClientFactory, EntityAddress, CancellationToken, Task> action)
    {
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Exception? failure = null;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateQueueAsync(queue, timeout.Token);
            await using var factory = new DirectServiceBusClientFactory();
            await factory.ConnectAsync(Profile(), timeout.Token);
            await action(admin, factory, new EntityAddress(EntityKind.Queue, queue), timeout.Token);
        }
        catch (Exception exception) { failure = exception; throw; }
        finally { await CleanupQueueAsync(admin, queue, failure); }
    }

    private async Task RunWithTopicAsync(string topic, Func<ServiceBusAdministrationClient, DirectServiceBusClientFactory, EntityAddress, CancellationToken, Task> action)
    {
        var admin = new ServiceBusAdministrationClient(ServiceBusEmulatorEnvironment.AdminConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Exception? failure = null;
        try
        {
            await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
            await admin.CreateTopicAsync(topic, timeout.Token);
            await admin.CreateSubscriptionAsync(topic, "first", timeout.Token);
            await admin.CreateSubscriptionAsync(topic, "second", timeout.Token);
            await using var factory = new DirectServiceBusClientFactory();
            await factory.ConnectAsync(Profile(), timeout.Token);
            await action(admin, factory, new EntityAddress(EntityKind.Subscription, "first", topic), timeout.Token);
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { if (await admin.TopicExistsAsync(topic, cleanup.Token)) await admin.DeleteTopicAsync(topic, cleanup.Token); }
            catch when (failure is not null) { }
        }
    }

    private static ConnectionProfile Profile() => new("daily-audit", ServiceBusEmulatorEnvironment.RuntimeConnectionString, ServiceBusEmulatorEnvironment.AdminConnectionString);

    private static MessageDelivery Delivery(EntityAddress source, ExplorerMessage message) =>
        new(new DeliveryIdentity(1, source, MessageBucket.DeadLetter, message.SequenceNumber), message);

    private static async Task<ReplayReservation> ReplayCurrentAsync(
        MessageDelivery delivery,
        ConnectionProfile profile,
        CancellationToken token,
        ReplayFamilyState? priorFamily = null,
        string? editedBody = null)
    {
        ReplayReservation reservation = ReplayLineage.Reserve(delivery, priorFamily is null ? [] : [priorFamily]);
        await using var sender = new ReplayCopySender(profile);
        await sender.SendAsync(
            ReplayLineage.Destination(delivery.Identity.Source),
            ReplayLineage.CreateMessage(delivery, reservation, editedBody),
            token);
        return reservation;
    }

    private static async Task<DlqDeleteResult> DeleteCurrentAsync(
        ConnectionProfile profile,
        CancellationToken token,
        params MessageDelivery[] deliveries)
    {
        var deleter = new DlqDeliveryDeleter(address => new DlqDeleteReceiver(profile, address));
        return await deleter.DeleteAsync(deliveries, token);
    }

    private static async Task MoveOneToDlqAsync(ServiceBusClient client, string queue, CancellationToken token)
    {
        await using ServiceBusReceiver receiver = client.CreateReceiver(queue);
        await receiver.DeadLetterMessageAsync(await ReceiveAsync(receiver, token), cancellationToken: token);
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveAsync(ServiceBusReceiver receiver, CancellationToken token) =>
        Assert.IsType<ServiceBusReceivedMessage>(await receiver.ReceiveMessageAsync(ReceiveWait, token));

    private static async Task<IReadOnlyList<ExplorerMessage>> PeekAsync(ServiceBusMessageService service, EntityAddress address, MessageBucket bucket, CancellationToken token)
    {
        var result = new List<ExplorerMessage>();
        long? cursor = null;
        for (int page = 0; page < 5; page++)
        {
            IReadOnlyList<ExplorerMessage> next = await service.PeekMessagesAsync(address, bucket, 100, cursor, token);
            if (next.Count == 0) return result;
            result.AddRange(next);
            cursor = checked(next[^1].SequenceNumber + 1);
        }
        throw new Xunit.Sdk.XunitException("Audit fixture did not exhaust its bounded peek pages.");
    }

    private static async Task EventuallyEmptyAsync(ServiceBusMessageService service, EntityAddress address, MessageBucket bucket, TimeSpan limit, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + limit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await PeekAsync(service, address, bucket, token)).Count == 0) return;
            await Task.Delay(250, token);
        }
        Assert.Empty(await PeekAsync(service, address, bucket, token));
    }

    private static async Task EventuallyBodyAsync(ServiceBusMessageService service, EntityAddress address, string body, TimeSpan limit, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + limit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await PeekAsync(service, address, MessageBucket.Active, token)).Any(message => message.Body == body)) return;
            await Task.Delay(250, token);
        }
        Assert.Contains(body, (await PeekAsync(service, address, MessageBucket.Active, token)).Select(message => message.Body));
    }

    private static async Task CleanupQueueAsync(ServiceBusAdministrationClient admin, string queue, Exception? failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { if (await admin.QueueExistsAsync(queue, timeout.Token)) await admin.DeleteQueueAsync(queue, timeout.Token); }
        catch when (failure is not null) { }
    }
}
