using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationDlqDeleteTests
{
    private static readonly EntityAddress Queue = new(EntityKind.Queue, "orders");

    [Fact]
    public async Task Completes_only_exact_selected_delivery_and_releases_unselected_after_scan()
    {
        var receiver = new Receiver([Message(9)], [Message(2)]);
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);

        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes).Status);
        Assert.Equal(new[] { "receive", "receive", "complete:2", "abandon:9", "dispose" }, receiver.Events);
        Assert.False(result.CleanupIncomplete);
    }

    [Fact]
    public async Task Reads_past_higher_sequence_numbers_to_find_late_dead_letter_arrival()
    {
        var receiver = new Receiver([Message(100), Message(101)], [Message(1)]);
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(1)], default);

        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes).Status);
        Assert.Equal(new long[] { 1 }, receiver.Completed);
        Assert.Equal(new long[] { 100, 101 }, receiver.Abandoned);
    }

    [Fact]
    public async Task Matching_sequence_with_different_payload_is_never_deleted()
    {
        var receiver = new Receiver([Message(2, "different")], []);
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);

        Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(result.Outcomes).Status);
        Assert.Empty(receiver.Completed);
        Assert.Equal(new long[] { 2 }, receiver.Abandoned);
    }

    [Fact]
    public async Task Empty_scan_reports_unavailable_without_settlement()
    {
        var receiver = new Receiver([]);
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);
        Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(result.Outcomes).Status);
        Assert.Empty(receiver.Completed);
        Assert.Empty(receiver.Abandoned);
    }

    [Fact]
    public async Task Receive_failure_releases_held_messages_and_reports_unavailable()
    {
        var receiver = new Receiver([Message(9)]) { FailReceiveCall = 2 };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);
        Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(result.Outcomes).Status);
        Assert.Equal(new long[] { 9 }, receiver.Abandoned);
        Assert.Equal("dispose", receiver.Events.Last());
    }

    [Fact]
    public async Task Failed_complete_is_uncertain_and_is_not_retried()
    {
        var receiver = new Receiver([Message(2)]) { FailComplete = true };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);
        Assert.Equal(DlqDeleteStatus.Uncertain, Assert.Single(result.Outcomes).Status);
        Assert.Equal(new long[] { 2 }, receiver.Completed);
    }

    [Fact]
    public async Task Later_uncertain_settlement_preserves_earlier_confirmed_target_without_retries()
    {
        var receiver = new Receiver([Message(2), Message(3)]) { FailCompleteSequence = 3 };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2), Delivery(3)], default);

        Assert.Collection(result.Outcomes,
            first =>
            {
                Assert.Equal(Delivery(2).Identity, first.Identity);
                Assert.Equal(DlqDeleteStatus.Confirmed, first.Status);
            },
            second =>
            {
                Assert.Equal(Delivery(3).Identity, second.Identity);
                Assert.Equal(DlqDeleteStatus.Uncertain, second.Status);
            });
        Assert.Equal(new long[] { 2, 3 }, receiver.Completed);
        Assert.Equal(new long[] { 3 }, receiver.Abandoned);
        Assert.Single(receiver.RequestedTakes);
    }

    [Fact]
    public async Task Cleanup_failure_preserves_confirmed_result()
    {
        var receiver = new Receiver([Message(9), Message(2)]) { FailAbandon = true };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);
        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes).Status);
        Assert.True(result.CleanupIncomplete);
        Assert.Equal("dispose", receiver.Events.Last());
    }

    [Fact]
    public async Task Receiver_disposal_failure_preserves_confirmed_result()
    {
        var receiver = new Receiver([Message(2)]) { FailDispose = true };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], default);
        Assert.Equal(DlqDeleteStatus.Confirmed, Assert.Single(result.Outcomes).Status);
        Assert.True(result.CleanupIncomplete);
    }

    [Fact]
    public async Task Scan_limit_reports_unavailable_and_releases_held_messages()
    {
        var receiver = new Receiver([Message(9), Message(10)]);
        var result = await new DlqDeliveryDeleter(_ => receiver, 2).DeleteAsync([Delivery(2)], default);
        Assert.Equal(DlqDeleteStatus.Unavailable, Assert.Single(result.Outcomes).Status);
        Assert.True(result.ScanLimitReached);
        Assert.Equal(new long[] { 9, 10 }, receiver.Abandoned);
        Assert.Single(receiver.RequestedTakes);
        Assert.Equal(2, receiver.RequestedTakes[0]);
    }

    [Fact]
    public async Task Cancellation_releases_held_locks_with_a_separate_live_token()
    {
        using var cancellation = new CancellationTokenSource();
        var receiver = new Receiver([Message(9)]) { BeforeReceive = call => { if (call == 2) cancellation.Cancel(); } };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2)], cancellation.Token);
        Assert.Equal(DlqDeleteStatus.NotAttempted, Assert.Single(result.Outcomes).Status);
        Assert.Equal(new long[] { 9 }, receiver.Abandoned);
        Assert.False(receiver.CleanupSawCanceledToken);
    }

    [Fact]
    public async Task Cancellation_as_receive_returns_owns_and_releases_every_returned_lock_without_completing()
    {
        using var cancellation = new CancellationTokenSource();
        var receiver = new Receiver([Message(2), Message(9), Message(3)])
        {
            AfterReceive = cancellation.Cancel
        };
        var result = await new DlqDeliveryDeleter(_ => receiver).DeleteAsync([Delivery(2), Delivery(3)], cancellation.Token);

        Assert.Equal(2, result.Outcomes.Count);
        Assert.All(result.Outcomes, outcome => Assert.Equal(DlqDeleteStatus.NotAttempted, outcome.Status));
        Assert.Empty(receiver.Completed);
        Assert.Equal(new long[] { 2, 9, 3 }, receiver.Abandoned);
        Assert.False(receiver.CleanupSawCanceledToken);
        Assert.False(result.CleanupIncomplete);
        Assert.Equal("dispose", receiver.Events.Last());
    }

    [Fact]
    public async Task Different_sources_with_same_sequence_are_settled_on_their_own_receivers()
    {
        var subscription = new EntityAddress(EntityKind.Subscription, "billing", "events");
        var queueReceiver = new Receiver([Message(2)]);
        var subscriptionReceiver = new Receiver([Message(2)]);
        var sources = new List<EntityAddress>();
        var deleter = new DlqDeliveryDeleter(source =>
        {
            sources.Add(source);
            return source == Queue ? queueReceiver : subscriptionReceiver;
        });
        var result = await deleter.DeleteAsync([Delivery(2), Delivery(2, subscription)], default);
        Assert.Equal(new[] { Queue, subscription }, sources);
        Assert.Equal(2, result.Outcomes.Count);
        Assert.All(result.Outcomes, outcome => Assert.Equal(DlqDeleteStatus.Confirmed, outcome.Status));
        Assert.Single(queueReceiver.Completed);
        Assert.Single(subscriptionReceiver.Completed);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("topic")]
    [InlineData("subscription")]
    [InlineData("generation")]
    [InlineData("duplicate")]
    public async Task Invalid_selection_is_rejected_before_any_receiver_is_created(string invalid)
    {
        var selected = new List<MessageDelivery> { Delivery(2) };
        selected.Add(invalid switch
        {
            "active" => Delivery(3, bucket: MessageBucket.Active),
            "topic" => Delivery(3, new(EntityKind.Topic, "events")),
            "subscription" => Delivery(3, new(EntityKind.Subscription, "billing")),
            "generation" => Delivery(3, generation: 2),
            _ => Delivery(2)
        });
        int created = 0;
        var deleter = new DlqDeliveryDeleter(_ => { created++; return new Receiver([]); });
        await Assert.ThrowsAnyAsync<ArgumentException>(() => deleter.DeleteAsync(selected, default));
        Assert.Equal(0, created);
    }

    private static MessageDelivery Delivery(long sequence, EntityAddress? source = null,
        MessageBucket bucket = MessageBucket.DeadLetter, long generation = 1) =>
        new(new(generation, source ?? Queue, bucket, sequence), MessageProjection.Create(Message(sequence)));

    private static ServiceBusReceivedMessage Message(long sequence, string body = "payload") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString(body),
            messageId: "same-id", sequenceNumber: sequence,
            enqueuedTime: DateTimeOffset.Parse("2026-09-12T12:00:00Z"));

    private sealed class Receiver(params ServiceBusReceivedMessage[][] batches) : IDlqDeleteReceiver
    {
        private int receiveCalls;
        public List<string> Events { get; } = [];
        public List<long> Completed { get; } = [];
        public List<long> Abandoned { get; } = [];
        public List<int> RequestedTakes { get; } = [];
        public int FailReceiveCall { get; init; }
        public bool FailComplete { get; init; }
        public long? FailCompleteSequence { get; init; }
        public bool FailAbandon { get; init; }
        public bool FailDispose { get; init; }
        public Action<int>? BeforeReceive { get; init; }
        public Action? AfterReceive { get; init; }
        public bool CleanupSawCanceledToken { get; private set; }

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken token)
        {
            Events.Add("receive");
            RequestedTakes.Add(take);
            receiveCalls++;
            BeforeReceive?.Invoke(receiveCalls);
            token.ThrowIfCancellationRequested();
            if (receiveCalls == FailReceiveCall) throw new InvalidOperationException("Receive failed.");
            IReadOnlyList<ServiceBusReceivedMessage> batch = receiveCalls <= batches.Length ? batches[receiveCalls - 1] : [];
            AfterReceive?.Invoke();
            return Task.FromResult(batch);
        }

        public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Events.Add($"complete:{message.SequenceNumber}");
            Completed.Add(message.SequenceNumber);
            if (FailComplete || FailCompleteSequence == message.SequenceNumber) throw new InvalidOperationException("Settlement outcome unknown.");
            return Task.CompletedTask;
        }

        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken token)
        {
            CleanupSawCanceledToken |= token.IsCancellationRequested;
            token.ThrowIfCancellationRequested();
            Events.Add($"abandon:{message.SequenceNumber}");
            Abandoned.Add(message.SequenceNumber);
            if (FailAbandon) throw new InvalidOperationException("Abandon failed.");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Events.Add("dispose");
            if (FailDispose) throw new InvalidOperationException("Dispose failed.");
            return ValueTask.CompletedTask;
        }
    }
}
