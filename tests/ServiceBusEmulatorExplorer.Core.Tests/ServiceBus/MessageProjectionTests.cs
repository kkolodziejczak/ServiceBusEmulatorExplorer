using Azure.Core.Amqp;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class MessageProjectionTests
{
    [Fact]
    public void Create_retains_original_bytes_when_body_is_not_valid_utf8()
    {
        byte[] bytes = [0xFF, 0x00, 0xC3, 0x28];
        var source = ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromBytes(bytes));

        ExplorerMessage message = MessageProjection.Create(source);

        Assert.NotNull(message.RawBody);
        Assert.Equal(bytes, message.RawBody.ToArray());
        Assert.Equal(bytes.Length, message.BodySizeBytes);
    }

    [Fact]
    public void Create_describes_a_value_body_instead_of_failing_the_page()
    {
        ExplorerMessage message = MessageProjection.Create(FromAmqp(AmqpMessageBody.FromValue(42)));

        Assert.False(message.HasDataBody);
        Assert.Null(message.RawBody);
        Assert.Equal("42", message.Body);
        Assert.Equal(0, message.BodySizeBytes);
    }

    [Fact]
    public void Create_describes_a_sequence_body_one_section_per_line()
    {
        ExplorerMessage message = MessageProjection.Create(FromAmqp(AmqpMessageBody.FromSequence(
            [new List<object> { "a", 1 }, new List<object> { 2.5 }])));

        Assert.False(message.HasDataBody);
        Assert.Equal($"[a, 1]{Environment.NewLine}[2.5]", message.Body);
    }

    [Fact]
    public void Create_marks_a_data_body_as_data()
    {
        Assert.True(MessageProjection.Create(ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("x"))).HasDataBody);
    }

    // Shaped like a broker delivery: a header with the delivery count and the sequence and enqueue annotations.
    private static ServiceBusReceivedMessage FromAmqp(AmqpMessageBody body)
    {
        var amqp = new AmqpAnnotatedMessage(body);
        amqp.Header.DeliveryCount = 0;
        amqp.MessageAnnotations["x-opt-sequence-number"] = 4L;
        amqp.MessageAnnotations["x-opt-enqueued-time"] = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc);
        return ServiceBusReceivedMessage.FromAmqpMessage(amqp, BinaryData.FromBytes(Guid.NewGuid().ToByteArray()));
    }

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

    [Fact]
    public void Create_projects_scheduled_enqueue_time_as_utc()
    {
        DateTimeOffset scheduled = new(2026, 7, 8, 14, 30, 0, TimeSpan.FromHours(2));
        ServiceBusReceivedMessage source = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("scheduled"),
            scheduledEnqueueTime: scheduled);

        ExplorerMessage message = MessageProjection.Create(source);

        Assert.Equal(
            scheduled.ToUniversalTime(),
            Assert.IsType<DateTimeOffset>(message.SystemProperties["ScheduledEnqueueTimeUtc"]));
    }

    [Fact]
    public void Create_omits_scheduled_enqueue_time_when_message_is_not_scheduled()
    {
        ServiceBusReceivedMessage source = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("immediate"));

        ExplorerMessage message = MessageProjection.Create(source);

        Assert.False(message.SystemProperties.ContainsKey("ScheduledEnqueueTimeUtc"));
    }
}
