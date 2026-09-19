using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationReplayLineageTests
{
    private static readonly EntityAddress Queue = new(EntityKind.Queue, "orders");

    [Fact]
    public void Independent_reservations_without_history_keep_one_family_but_unique_send_ids()
    {
        ReplayReservation first = ReplayLineage.Reserve(Delivery(), []);
        ReplayReservation second = ReplayLineage.Reserve(Delivery(generation: 99), []);

        Assert.Equal(first.Family.FamilyId, second.Family.FamilyId);
        Assert.Equal(first.Family.RootFingerprint, second.Family.RootFingerprint);
        Assert.Equal(1, first.Family.LastAttempt);
        Assert.Equal(1, second.Family.LastAttempt);
        Assert.NotEqual(first.MessageId, second.MessageId);
    }

    [Fact]
    public void Reserving_original_again_after_restart_increments_persisted_family()
    {
        MessageDelivery original = Delivery();
        ReplayReservation first = ReplayLineage.Reserve(original, []);
        var restarted = new ReplayFamilyState(first.Family.FamilyId, first.Family.RootFingerprint,
            first.Family.OriginalMessageId, first.Family.OriginalSource, first.Family.LastAttempt);

        ReplayReservation next = ReplayLineage.Reserve(Delivery(generation: 99), [restarted]);

        Assert.Equal(1, first.Family.LastAttempt);
        Assert.Equal(2, next.Family.LastAttempt);
        Assert.Equal(first.Family.FamilyId, next.Family.FamilyId);
        Assert.Equal(first.Family.RootFingerprint, next.Family.RootFingerprint);
        Assert.Contains("-replay-2-", next.MessageId);
        Assert.NotEqual(first.MessageId, next.MessageId);
    }

    [Fact]
    public void Same_message_id_is_not_a_family_identity()
    {
        ReplayFamilyState existing = ReplayLineage.Reserve(Delivery(), []).Family;
        MessageDelivery[] unrelated =
        [
            Delivery(sequence: 2),
            Delivery(body: "changed"),
            Delivery(source: new(EntityKind.Queue, "other")),
            Delivery(enqueued: DateTimeOffset.Parse("2026-09-12T13:00:00Z"))
        ];

        foreach (MessageDelivery delivery in unrelated)
        {
            ReplayReservation reservation = ReplayLineage.Reserve(delivery, [existing]);
            Assert.NotEqual(existing.FamilyId, reservation.Family.FamilyId);
            Assert.NotEqual(existing.RootFingerprint, reservation.Family.RootFingerprint);
            Assert.Equal(1, reservation.Family.LastAttempt);
        }
    }

    [Fact]
    public void Fingerprint_uses_original_bytes_even_when_decoded_text_is_identical()
    {
        MessageDelivery first = Delivery(raw: BinaryData.FromBytes(new byte[] { 0xFF }));
        MessageDelivery second = Delivery(raw: BinaryData.FromBytes(new byte[] { 0xFE }));
        ReplayFamilyState existing = ReplayLineage.Reserve(first, []).Family;

        Assert.NotEqual(existing.RootFingerprint, ReplayLineage.Reserve(second, [existing]).Family.RootFingerprint);
    }

    [Fact]
    public void Unedited_copy_preserves_bytes_and_routing_metadata_without_mutating_original()
    {
        byte[] bytes = [0, 255, 128, 32];
        MessageDelivery original = Delivery(raw: BinaryData.FromBytes(bytes));
        ReplayReservation reservation = ReplayLineage.Reserve(original, []);

        ServiceBusMessage copy = ReplayLineage.CreateMessage(original, reservation);

        Assert.Equal(bytes, copy.Body.ToArray());
        Assert.Equal(reservation.MessageId, copy.MessageId);
        Assert.Equal("correlation", copy.CorrelationId);
        Assert.Equal("session", copy.SessionId);
        Assert.Equal("application/octet-stream", copy.ContentType);
        Assert.Equal("subject", copy.Subject);
        Assert.Equal("session", copy.PartitionKey);
        Assert.Equal("reply", copy.ReplyTo);
        Assert.Equal("destination", copy.To);
        Assert.Equal("reply-session", copy.ReplyToSessionId);
        Assert.Equal("transaction", copy.TransactionPartitionKey);
        Assert.Equal(TimeSpan.FromHours(3), copy.TimeToLive);
        Assert.Equal(42, copy.ApplicationProperties["business-number"]);
        Assert.Single(original.Message.ApplicationProperties);
        Assert.Equal("original", original.Message.MessageId);
    }

    [Fact]
    public void Edited_replay_from_another_subscription_keeps_root_lineage_and_next_attempt()
    {
        var firstSource = new EntityAddress(EntityKind.Subscription, "billing", "orders");
        MessageDelivery original = Delivery(source: firstSource);
        ReplayReservation first = ReplayLineage.Reserve(original, []);
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, first, "{\"fixed\":true}");
        MessageDelivery copy = Delivery(sequence: 91, source: new(EntityKind.Subscription, "audit", "orders"),
            body: sent.Body.ToString(), messageId: sent.MessageId,
            properties: sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));

        ReplayReservation next = ReplayLineage.Reserve(copy, [first.Family]);

        Assert.Equal("{\"fixed\":true}", sent.Body.ToString());
        Assert.Equal(first.Family.FamilyId, next.Family.FamilyId);
        Assert.Equal(first.Family.RootFingerprint, next.Family.RootFingerprint);
        Assert.Equal("original", next.Family.OriginalMessageId);
        Assert.Equal(firstSource, next.Family.OriginalSource);
        Assert.Equal(2, next.Family.LastAttempt);
    }

    [Fact]
    public void Long_original_ids_still_leave_room_for_attempt_and_unique_suffix()
    {
        MessageDelivery original = Delivery(messageId: new string('x', 128));
        ReplayFamilyState family = ReplayLineage.Reserve(original, []).Family;
        ReplayReservation first = ReplayLineage.Reserve(original, [family]);
        ReplayReservation parallel = ReplayLineage.Reserve(original, [family]);

        Assert.InRange(first.MessageId.Length, 1, 128);
        Assert.Contains("-replay-2-", first.MessageId);
        Assert.NotEqual(first.MessageId, parallel.MessageId);
    }

    [Fact]
    public void Attempt_overflow_is_rejected_instead_of_wrapping()
    {
        MessageDelivery original = Delivery();
        ReplayFamilyState family = ReplayLineage.Reserve(original, []).Family with { LastAttempt = long.MaxValue };

        Assert.Throws<OverflowException>(() => ReplayLineage.Reserve(original, [family]));
    }

    [Fact]
    public void Partial_or_malformed_lineage_cannot_silently_start_a_new_family()
    {
        MessageDelivery original = Delivery();
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, ReplayLineage.Reserve(original, []));
        string[] reserved = [ReplayLineage.FamilyProperty, ReplayLineage.RootProperty,
            ReplayLineage.OriginalIdProperty, ReplayLineage.SourceProperty, ReplayLineage.AttemptProperty];
        foreach (string key in reserved)
        {
            var missing = sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
            missing.Remove(key);
            Assert.Throws<ArgumentException>(() => ReplayLineage.Reserve(Delivery(properties: missing), []));
            var invalid = sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
            invalid[key] = false;
            Assert.Throws<ArgumentException>(() => ReplayLineage.Reserve(Delivery(properties: invalid), []));
        }
    }

    [Fact]
    public void Received_copy_with_later_attempt_advances_past_older_saved_state()
    {
        MessageDelivery original = Delivery();
        ReplayReservation first = ReplayLineage.Reserve(original, []);
        ReplayReservation later = ReplayLineage.Reserve(original, [first.Family with { LastAttempt = 8 }]);
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, later);
        MessageDelivery received = Delivery(properties: sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));

        ReplayReservation next = ReplayLineage.Reserve(received, [first.Family]);

        Assert.Equal(10, next.Family.LastAttempt);
        Assert.Equal(first.Family.FamilyId, next.Family.FamilyId);
    }

    [Fact]
    public void Conflicting_saved_and_received_family_identity_is_rejected()
    {
        MessageDelivery original = Delivery();
        ReplayReservation reservation = ReplayLineage.Reserve(original, []);
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, reservation);
        MessageDelivery received = Delivery(properties: sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));

        Assert.Throws<ArgumentException>(() => ReplayLineage.Reserve(received,
            [reservation.Family with { FamilyId = Guid.NewGuid() }]));
    }

    [Fact]
    public void Received_family_id_cannot_replace_its_saved_root_or_attempt_counter()
    {
        MessageDelivery original = Delivery();
        ReplayReservation reservation = ReplayLineage.Reserve(original, []);
        ReplayFamilyState saved = reservation.Family with { LastAttempt = 17 };
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, reservation);
        var properties = sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        string differentRoot = ReplayLineage.Reserve(Delivery(sequence: 500), []).Family.RootFingerprint;
        Assert.NotEqual(saved.RootFingerprint, differentRoot);
        properties[ReplayLineage.RootProperty] = differentRoot;
        ReplayFamilyState[] families = [saved];

        Assert.Throws<ArgumentException>(() => ReplayLineage.Reserve(Delivery(properties: properties), families));

        Assert.Same(saved, Assert.Single(families));
        Assert.Equal(17, families[0].LastAttempt);
        Assert.Equal(reservation.Family.RootFingerprint, families[0].RootFingerprint);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Root_case_differences_reuse_saved_family_and_normalize_result(bool lowercaseMetadata, bool lowercaseSaved)
    {
        MessageDelivery original = Delivery();
        ReplayReservation reservation = ReplayLineage.Reserve(original, []);
        string canonicalRoot = reservation.Family.RootFingerprint;
        Assert.NotEqual(canonicalRoot, canonicalRoot.ToLowerInvariant());
        ReplayFamilyState saved = reservation.Family with
        {
            RootFingerprint = lowercaseSaved ? canonicalRoot.ToLowerInvariant() : canonicalRoot,
            LastAttempt = 7
        };
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, reservation);
        var properties = sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        if (lowercaseMetadata) properties[ReplayLineage.RootProperty] = canonicalRoot.ToLowerInvariant();

        ReplayReservation next = ReplayLineage.Reserve(Delivery(properties: properties), [saved]);

        Assert.Equal(saved.FamilyId, next.Family.FamilyId);
        Assert.Equal(canonicalRoot, next.Family.RootFingerprint);
        Assert.Equal(8, next.Family.LastAttempt);
    }

    [Fact]
    public void Received_lineage_cannot_change_the_saved_original_source()
    {
        MessageDelivery original = Delivery();
        ReplayReservation reservation = ReplayLineage.Reserve(original, []);
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, reservation);
        var properties = sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        properties[ReplayLineage.SourceProperty] = System.Text.Json.JsonSerializer.Serialize(new EntityAddress(EntityKind.Queue, "other"));

        Assert.Throws<ArgumentException>(() => ReplayLineage.Reserve(Delivery(properties: properties), [reservation.Family]));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Kind\":2,\"Name\":\"billing\"}")]
    [InlineData("{\"Kind\":1,\"Name\":\"orders\"}")]
    public void Invalid_receiving_source_in_lineage_is_rejected_without_saved_history(string sourceJson)
    {
        MessageDelivery original = Delivery();
        ServiceBusMessage sent = ReplayLineage.CreateMessage(original, ReplayLineage.Reserve(original, []));
        var properties = sent.ApplicationProperties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        properties[ReplayLineage.SourceProperty] = sourceJson;

        Assert.Throws<ArgumentException>(() => ReplayLineage.Reserve(Delivery(properties: properties), []));
    }

    [Fact]
    public void Replay_destinations_are_queue_or_parent_topic_only()
    {
        Assert.Equal(Queue, ReplayLineage.Destination(Queue));
        Assert.Equal(new(EntityKind.Topic, "orders"),
            ReplayLineage.Destination(new(EntityKind.Subscription, "billing", "orders")));
        Assert.ThrowsAny<ArgumentException>(() => ReplayLineage.Destination(new(EntityKind.Topic, "orders")));
        Assert.ThrowsAny<ArgumentException>(() => ReplayLineage.Destination(new(EntityKind.Subscription, "billing")));
        Assert.ThrowsAny<ArgumentException>(() => ReplayLineage.Reserve(Delivery(bucket: MessageBucket.Active), []));
    }

    private static MessageDelivery Delivery(long sequence = 1, string body = "body", EntityAddress? source = null,
        long generation = 1, DateTimeOffset? enqueued = null, BinaryData? raw = null, string messageId = "original",
        IReadOnlyDictionary<string, object?>? properties = null, MessageBucket bucket = MessageBucket.DeadLetter)
    {
        var message = new ExplorerMessage(messageId, sequence, body, body, body.Length,
            enqueued ?? DateTimeOffset.Parse("2026-09-12T12:00:00Z"), null, 10,
            "application/octet-stream", "correlation", "session", "subject",
            properties ?? new Dictionary<string, object?> { ["business-number"] = 42 },
            new Dictionary<string, object?>
            {
                ["PartitionKey"] = "session", ["ReplyTo"] = "reply", ["To"] = "destination",
                ["ReplyToSessionId"] = "reply-session", ["TransactionPartitionKey"] = "transaction",
                ["TimeToLive"] = TimeSpan.FromHours(3)
            })
        { RawBody = raw };
        return new(new(generation, source ?? Queue, bucket, sequence), message);
    }
}
