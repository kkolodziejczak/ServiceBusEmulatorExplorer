using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class MessageProjectionTests
{
    [Fact]
    public void Create_preserves_body_properties_and_normalizes_dates_to_utc()
    {
        ServiceBusReceivedMessage source = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromBytes([0x48, 0x69]),
            messageId: "message-1",
            sessionId: "session-1",
            correlationId: "correlation-1",
            subject: "subject-1",
            contentType: "application/octet-stream",
            properties: new Dictionary<string, object> { ["custom"] = "value" },
            deliveryCount: 3,
            sequenceNumber: 42,
            enqueuedTime: new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.FromHours(2)));

        ExplorerMessage message = MessageProjection.Create(source);

        Assert.Equal("message-1", message.MessageId);
        Assert.Equal(42, message.SequenceNumber);
        Assert.Equal("Hi", message.Body);
        Assert.Equal("Hi", message.BodyPreview);
        Assert.Equal(2, message.BodySizeBytes);
        Assert.Equal("subject-1", message.Subject);
        Assert.Equal(TimeSpan.Zero, message.EnqueuedTime?.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero), message.EnqueuedTime);
        Assert.Equal("value", message.ApplicationProperties["custom"]);
        Assert.Equal(42L, message.SystemProperties["SequenceNumber"]);
    }
}
