using ServiceBusEmulatorExplorer.Core.Messaging;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class DeadLetterReplayRequestFactoryTests
{
    [Fact]
    public void CreateCopyRequest_targets_queue_and_keeps_original_dlq_message()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders");
        ExplorerMessage message = CreateMessage(sequenceNumber: 42);

        ReplayRequest request = DeadLetterReplayRequestFactory.CreateCopyRequest(queue, message);

        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), request.Source);
        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), request.Destination);
        Assert.Equal(42, request.SequenceNumber);
        Assert.Null(request.EditedBody);
        Assert.Null(request.ApplicationProperties);
        Assert.Equal(ReplayIdPolicy.NewGuid, request.IdPolicy);
        Assert.False(request.DeleteOriginal);
    }

    [Fact]
    public void CreateEditedRequest_targets_subscription_topic_and_keeps_original_dlq_message()
    {
        ServiceBusEntityNode subscription = CreateEntity(EntityKind.Subscription, "billing", "events");
        ExplorerMessage message = CreateMessage(sequenceNumber: 99);
        var properties = new Dictionary<string, object?> { ["attempt"] = 2 };
        var edits = new ReplayMessageEdits(
            EditedBody: "edited body",
            ContentType: "application/json",
            CorrelationId: "correlation-1",
            SessionId: "session-1",
            Subject: "edited-subject",
            ApplicationProperties: properties);

        ReplayRequest request = DeadLetterReplayRequestFactory.CreateEditedRequest(subscription, message, edits);

        Assert.Equal(new EntityAddress(EntityKind.Subscription, "billing", "events"), request.Source);
        Assert.Equal(new EntityAddress(EntityKind.Topic, "events"), request.Destination);
        Assert.Equal(99, request.SequenceNumber);
        Assert.Equal("edited body", request.EditedBody);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("correlation-1", request.CorrelationId);
        Assert.Equal("session-1", request.SessionId);
        Assert.Equal("edited-subject", request.Subject);
        Assert.Same(properties, request.ApplicationProperties);
        Assert.Equal(ReplayIdPolicy.NewGuid, request.IdPolicy);
        Assert.False(request.DeleteOriginal);
    }

    [Fact]
    public void CreateCopyRequest_rejects_topic_dlq_source()
    {
        ServiceBusEntityNode topic = CreateEntity(EntityKind.Topic, "events");
        ExplorerMessage message = CreateMessage(sequenceNumber: 1);

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => DeadLetterReplayRequestFactory.CreateCopyRequest(topic, message));

        Assert.Contains("not topics", ex.Message);
    }

    [Fact]
    public void CreateCopyRequest_requires_topic_name_for_subscription_source()
    {
        ServiceBusEntityNode subscription = CreateEntity(EntityKind.Subscription, "billing");
        ExplorerMessage message = CreateMessage(sequenceNumber: 1);

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => DeadLetterReplayRequestFactory.CreateCopyRequest(subscription, message));

        Assert.Contains("Topic name is required", ex.Message);
    }

    private static ServiceBusEntityNode CreateEntity(EntityKind kind, string name, string? topicName = null)
    {
        return new ServiceBusEntityNode(
            kind,
            name,
            topicName,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(
                name,
                "Active",
                CreatedAtUtc: null,
                UpdatedAtUtc: null,
                LockDuration: null,
                MaxDeliveryCount: null,
                DefaultMessageTimeToLive: null,
                RequiresSession: null,
                RequiresDuplicateDetection: null));
    }

    private static ExplorerMessage CreateMessage(long sequenceNumber)
    {
        return new ExplorerMessage(
            "message-1",
            sequenceNumber,
            "body",
            "body",
            BodySizeBytes: 4,
            EnqueuedTime: null,
            ExpiresAt: null,
            DeliveryCount: 1,
            ContentType: null,
            CorrelationId: null,
            SessionId: null,
            Subject: null,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>());
    }
}
