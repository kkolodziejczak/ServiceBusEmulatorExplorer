using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Messaging;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class DeadLetterReplayMessageMapperTests
{
    [Fact]
    public void ToReplayMessage_assigns_new_message_id_and_uses_edited_values()
    {
        ServiceBusReceivedMessage original = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("original body"),
            messageId: "original-id",
            contentType: "text/plain",
            correlationId: "original-correlation",
            subject: "original-subject",
            properties: new Dictionary<string, object> { ["original"] = "value" },
            sequenceNumber: 42);
        var request = new ReplayRequest(
            new EntityAddress(EntityKind.Queue, "orders"),
            new EntityAddress(EntityKind.Queue, "orders"),
            SequenceNumber: 42,
            EditedBody: "edited body",
            ContentType: "application/json",
            CorrelationId: "edited-correlation",
            SessionId: "session-1",
            Subject: "edited-subject",
            new Dictionary<string, object?> { ["edited"] = "true" },
            ReplayIdPolicy.NewGuid,
            DeleteOriginal: false);

        ServiceBusMessage message = DeadLetterReplayMessageMapper.ToReplayMessage(request, original);

        Assert.Equal("edited body", message.Body.ToString());
        Assert.NotEqual("original-id", message.MessageId);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal("edited-correlation", message.CorrelationId);
        Assert.Equal("session-1", message.SessionId);
        Assert.Equal("edited-subject", message.Subject);
        Assert.False(message.ApplicationProperties.ContainsKey("original"));
        Assert.Equal("true", message.ApplicationProperties["edited"]);
    }

    [Fact]
    public void ToReplayMessage_uses_original_body_and_metadata_when_request_values_are_empty()
    {
        ServiceBusReceivedMessage original = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("original body"),
            messageId: "original-id",
            contentType: "text/plain",
            correlationId: "original-correlation",
            subject: "original-subject",
            properties: new Dictionary<string, object> { ["count"] = 3 },
            sequenceNumber: 42);
        var request = new ReplayRequest(
            new EntityAddress(EntityKind.Queue, "orders"),
            new EntityAddress(EntityKind.Queue, "orders"),
            SequenceNumber: 42,
            EditedBody: null,
            ContentType: "",
            CorrelationId: "",
            SessionId: null,
            Subject: "",
            ApplicationProperties: null,
            ReplayIdPolicy.NewGuid,
            DeleteOriginal: false);

        ServiceBusMessage message = DeadLetterReplayMessageMapper.ToReplayMessage(request, original);

        Assert.Equal("original body", message.Body.ToString());
        Assert.Equal("text/plain", message.ContentType);
        Assert.Equal("original-correlation", message.CorrelationId);
        Assert.Equal("original-subject", message.Subject);
        Assert.Equal(3, message.ApplicationProperties["count"]);
    }

    [Fact]
    public void EnsureValid_rejects_topic_source_for_dlq_operations()
    {
        var request = new DeleteDeadLetterMessagesRequest(
            new EntityAddress(EntityKind.Topic, "events"),
            [1]);

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => DeadLetterReplayMessageMapper.EnsureValid(request));

        Assert.Contains("queues and subscriptions", ex.Message);
    }

    [Fact]
    public void EnsureValid_rejects_replay_delete_original()
    {
        var request = new ReplayRequest(
            new EntityAddress(EntityKind.Queue, "orders"),
            new EntityAddress(EntityKind.Queue, "orders"),
            SequenceNumber: 42,
            EditedBody: "body",
            ContentType: null,
            CorrelationId: null,
            SessionId: null,
            Subject: null,
            new Dictionary<string, object?>(),
            ReplayIdPolicy.NewGuid,
            DeleteOriginal: true);

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => DeadLetterReplayMessageMapper.EnsureValid(request));

        Assert.Contains("separate MVP operations", ex.Message);
    }
}
