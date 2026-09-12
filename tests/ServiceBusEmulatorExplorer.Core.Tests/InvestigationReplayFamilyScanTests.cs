using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationReplayFamilyScanTests
{
    private static readonly EntityAddress Queue = new(EntityKind.Queue, "orders");
    private static readonly EntityAddress Topic = new(EntityKind.Topic, "events");
    private static readonly EntityAddress Billing = new(EntityKind.Subscription, "billing", "events");
    private static readonly EntityAddress Analytics = new(EntityKind.Subscription, "analytics", "events");

    [Fact]
    public async Task Original_keeps_family_even_after_connection_generation_changes()
    {
        var original = Delivery();
        var messages = new Messages();
        messages.Set(Queue, original.Message);
        var result = await Scan(messages, original, Snapshot(Queue));
        Assert.Equal(ReplayFamilyPresence.Present, result.Presence);
        Assert.Equal(1, result.ScannedDeliveries);
        Assert.All(messages.Calls, call => Assert.Equal(MessageBucket.DeadLetter, call.Bucket));
    }

    [Fact]
    public async Task Explicit_replay_with_different_message_id_keeps_family()
    {
        var original = Delivery();
        var messages = new Messages();
        messages.Set(Queue, Copy(original, Queue, 80).Message);
        Assert.Equal(ReplayFamilyPresence.Present, (await Scan(messages, original, Snapshot(Queue))).Presence);
    }

    [Fact]
    public async Task Same_message_id_from_another_original_does_not_keep_family()
    {
        var messages = new Messages();
        messages.Set(Queue, Delivery(sequence: 2).Message);
        var result = await Scan(messages, Delivery(), Snapshot(Queue));
        Assert.Equal(ReplayFamilyPresence.Absent, result.Presence);
        Assert.Equal(1, result.ScannedDeliveries);
        Assert.Equal(2, messages.Calls.Count);
    }

    [Fact]
    public async Task Queue_scan_does_not_inspect_other_receiving_entities()
    {
        var messages = new Messages();
        var other = new EntityAddress(EntityKind.Queue, "other");
        messages.Set(other, Copy(Delivery(), other, 2).Message);
        Assert.Equal(ReplayFamilyPresence.Absent,
            (await Scan(messages, Delivery(), Snapshot(Queue, other, Topic, Billing))).Presence);
        Assert.Equal(Queue, Assert.Single(messages.Calls).Source);
    }

    [Fact]
    public async Task Topic_fanout_copy_in_another_subscription_keeps_family()
    {
        var original = Delivery(Billing);
        var messages = new Messages();
        messages.Set(Analytics, Copy(original, Analytics, 99).Message);
        Assert.Equal(ReplayFamilyPresence.Present,
            (await Scan(messages, original, Snapshot(Topic, Billing, Analytics))).Presence);
        Assert.Contains(messages.Calls, call => call.Source == Analytics);
        Assert.DoesNotContain(messages.Calls, call => call.Source.Kind == EntityKind.Topic);
    }

    [Fact]
    public async Task Topic_prefix_is_not_topic_identity()
    {
        var original = Delivery(Billing);
        var other = new EntityAddress(EntityKind.Subscription, "billing", "events-archive");
        var messages = new Messages();
        messages.Set(other, Copy(original, other, 3).Message);
        Assert.Equal(ReplayFamilyPresence.Absent,
            (await Scan(messages, original, Snapshot(Topic, Billing, Analytics,
                new(EntityKind.Topic, "events-archive"), other))).Presence);
        Assert.DoesNotContain(messages.Calls, call => call.Source == other);
        Assert.Contains(messages.Calls, call => call.Source == Analytics);
    }

    [Fact]
    public async Task Short_pages_continue_until_actual_empty_page()
    {
        var messages = new Messages { PageSize = 37 };
        messages.Set(Queue, Enumerable.Range(2, 205).Select(index => Delivery(sequence: index).Message).ToArray());
        var result = await Scan(messages, Delivery(), Snapshot(Queue), 300);
        Assert.Equal(ReplayFamilyPresence.Absent, result.Presence);
        Assert.Equal(205, result.ScannedDeliveries);
        Assert.Equal(7, messages.Calls.Count);
        Assert.Equal(0L, messages.Calls[0].From);
        Assert.All(messages.Calls, call => Assert.InRange(call.Take, 1, 100));
    }

    [Fact]
    public async Task Exact_budget_does_not_prove_absence_without_empty_page()
    {
        var messages = new Messages();
        messages.Set(Queue, Enumerable.Range(2, 100).Select(index => Delivery(sequence: index).Message).ToArray());
        var result = await Scan(messages, Delivery(), Snapshot(Queue), 100);
        Assert.Equal(ReplayFamilyPresence.Incomplete, result.Presence);
        Assert.Equal(100, result.ScannedDeliveries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_or_during_peek_cannot_prove_absence(bool duringPeek)
    {
        using var cancellation = new CancellationTokenSource();
        var messages = new Messages();
        if (duringPeek) messages.BeforeRead = () => cancellation.Cancel();
        else cancellation.Cancel();
        var result = await Scan(messages, Delivery(), Snapshot(Queue), token: cancellation.Token);
        Assert.Equal(ReplayFamilyPresence.Incomplete, result.Presence);
    }

    [Fact]
    public async Task Broker_error_cannot_prove_absence()
    {
        var messages = new Messages { BeforeRead = () => throw new InvalidOperationException("Unavailable") };
        Assert.Equal(ReplayFamilyPresence.Incomplete,
            (await Scan(messages, Delivery(), Snapshot(Queue))).Presence);
    }

    [Fact]
    public async Task Partial_discovery_cannot_prove_absence_even_if_original_source_was_discovered()
    {
        var messages = new Messages();
        var discovery = Snapshot(Queue) with { IsComplete = false, Issues = ["missing entities"] };
        Assert.Equal(ReplayFamilyPresence.Incomplete, (await Scan(messages, Delivery(), discovery)).Presence);
        Assert.Empty(messages.Calls);
    }

    [Fact]
    public async Task Missing_queue_cannot_prove_absence()
    {
        var messages = new Messages();
        Assert.Equal(ReplayFamilyPresence.Incomplete, (await Scan(messages, Delivery(), Snapshot())).Presence);
        Assert.Empty(messages.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_original_subscription_or_parent_topic_cannot_prove_absence(bool missingTopic)
    {
        var messages = new Messages();
        var discovery = missingTopic ? Snapshot(Billing, Analytics) : Snapshot(Topic, Analytics);
        Assert.Equal(ReplayFamilyPresence.Incomplete, (await Scan(messages, Delivery(Billing), discovery)).Presence);
        Assert.Empty(messages.Calls);
    }

    [Fact]
    public async Task Malformed_lineage_cannot_be_treated_as_unrelated()
    {
        var messages = new Messages();
        messages.Set(Queue, Delivery(sequence: 4, properties: new Dictionary<string, object?>
        { [ReplayLineage.FamilyProperty] = "broken" }).Message);
        Assert.Equal(ReplayFamilyPresence.Incomplete,
            (await Scan(messages, Delivery(), Snapshot(Queue))).Presence);
    }

    [Fact]
    public async Task Saved_family_id_with_a_different_valid_root_is_incomplete()
    {
        var original = Delivery();
        var root = ReplayLineage.Reserve(original, []).Family.RootFingerprint;
        string conflictingRoot = (root[0] == 'A' ? "B" : "A") + root[1..];
        await AssertConflictingLineage(original, ReplayLineage.RootProperty, conflictingRoot);
    }

    [Fact]
    public async Task Saved_root_with_a_different_valid_family_id_is_incomplete()
    {
        await AssertConflictingLineage(Delivery(), ReplayLineage.FamilyProperty, Guid.NewGuid().ToString("N"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Matching_root_and_family_id_with_conflicting_original_identity_is_incomplete(bool differentSource)
    {
        string key = differentSource ? ReplayLineage.SourceProperty : ReplayLineage.OriginalIdProperty;
        string value = differentSource
            ? System.Text.Json.JsonSerializer.Serialize(new EntityAddress(EntityKind.Queue, "other"))
            : "different-original-message";
        await AssertConflictingLineage(Delivery(), key, value);
    }

    private static async Task AssertConflictingLineage(MessageDelivery original, string key, string value)
    {
        var copy = Copy(original, Queue, 80);
        var properties = copy.Message.ApplicationProperties.ToDictionary(pair => pair.Key, pair => pair.Value);
        properties[key] = value;
        var messages = new Messages();
        messages.Set(Queue, Delivery(sequence: 80, messageId: copy.Message.MessageId, properties: properties).Message);
        Assert.Equal(ReplayFamilyPresence.Incomplete,
            (await Scan(messages, original, Snapshot(Queue))).Presence);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task Invalid_or_overflowing_sequence_cannot_prove_absence(long sequence)
    {
        var messages = new Messages { IgnoreCursor = true };
        messages.Set(Queue, Delivery(sequence: sequence).Message);
        Assert.Equal(ReplayFamilyPresence.Incomplete,
            (await Scan(messages, Delivery(), Snapshot(Queue))).Presence);
    }

    [Fact]
    public async Task Broker_page_that_does_not_advance_cursor_is_incomplete()
    {
        var messages = new Messages { IgnoreCursor = true };
        messages.Set(Queue, Delivery(sequence: 3).Message);
        Assert.Equal(ReplayFamilyPresence.Incomplete,
            (await Scan(messages, Delivery(), Snapshot(Queue))).Presence);
        Assert.InRange(messages.Calls.Count, 1, 2);
    }

    private static Task<ReplayFamilyScanResult> Scan(Messages messages, MessageDelivery original,
        EntityDiscoverySnapshot discovery, int budget = 1000, CancellationToken token = default)
        => new ReplayFamilyScanner(messages).ScanAsync(ReplayLineage.Reserve(original, []).Family,
            discovery, 99, budget, token);

    private static MessageDelivery Copy(MessageDelivery original, EntityAddress source, long sequence)
    {
        var reservation = ReplayLineage.Reserve(original, []);
        var message = ReplayLineage.CreateMessage(original, reservation);
        return Delivery(source, sequence, message.MessageId,
            message.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
    }

    private static MessageDelivery Delivery(EntityAddress? source = null, long sequence = 1,
        string messageId = "original", IReadOnlyDictionary<string, object?>? properties = null)
    {
        var message = new ExplorerMessage(messageId, sequence, "body", "body", 4,
            DateTimeOffset.Parse("2026-09-12T12:00:00Z"), null, 10, null, null, null, null,
            properties ?? new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new(new(1, source ?? Queue, MessageBucket.DeadLetter, sequence), message);
    }

    private static EntityDiscoverySnapshot Snapshot(params EntityAddress[] addresses)
        => new(addresses.Select(address => new EntityObservation(
            new DiscoveredEntity(address.Kind, address.Name, address.TopicName,
                new(address.Name, "Active", null, null, null, null, null, null, null)),
            new(new(0, CountAvailability.Known), new(0, CountAvailability.Known),
                new(null, CountAvailability.NotSupported)))).ToArray(), DateTimeOffset.UtcNow, true, []);

    private sealed class Messages : IServiceBusMessageService
    {
        private readonly Dictionary<EntityAddress, ExplorerMessage[]> rows = [];
        public List<(EntityAddress Source, MessageBucket Bucket, int Take, long? From)> Calls { get; } = [];
        public int PageSize { get; init; } = 100;
        public bool IgnoreCursor { get; init; }
        public Action? BeforeRead { get; set; }
        public void Set(EntityAddress source, params ExplorerMessage[] messages) => rows[source] = messages;

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            Calls.Add((address, bucket, take, fromSequenceNumber));
            BeforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(rows.GetValueOrDefault(address, [])
                .Where(row => IgnoreCursor || row.SequenceNumber >= (fromSequenceNumber ?? 0))
                .Take(Math.Min(take, PageSize)).ToArray());
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Absence scans must never send messages.");
    }
}
