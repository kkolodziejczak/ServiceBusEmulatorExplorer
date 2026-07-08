using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class SendMessageRequestMapperTests
{
    [Fact]
    public void ToServiceBusMessage_maps_body_metadata_and_application_properties()
    {
        var command = new SendMessageCommand(
            new EntityAddress(EntityKind.Queue, "orders"),
            "payload",
            ContentType: " application/json ",
            CorrelationId: " correlation-1 ",
            SessionId: " session-1 ",
            Subject: " subject-1 ",
            ApplicationProperties: new Dictionary<string, object?>
            {
                [" kind "] = "unit",
                ["count"] = 3
            });

        Azure.Messaging.ServiceBus.ServiceBusMessage message = SendMessageRequestMapper.ToServiceBusMessage(command);

        Assert.Equal("payload", message.Body.ToString());
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal("correlation-1", message.CorrelationId);
        Assert.Equal("session-1", message.SessionId);
        Assert.Equal("subject-1", message.Subject);
        Assert.Equal("unit", message.ApplicationProperties["kind"]);
        Assert.Equal(3, message.ApplicationProperties["count"]);
    }

    [Fact]
    public void EnsureValid_rejects_subscription_destination()
    {
        var command = new SendMessageCommand(
            new EntityAddress(EntityKind.Subscription, "billing", "events"),
            "payload");

        ArgumentException ex = Assert.Throws<ArgumentException>(() => SendMessageRequestMapper.EnsureValid(command));

        Assert.Contains("Messages can be sent to queues and topics", ex.Message);
    }
}
