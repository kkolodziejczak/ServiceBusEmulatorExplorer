using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class MessageProjectionDeadLetterTests
{
    [Fact]
    public void Forwarded_dead_letter_keeps_its_dead_letter_source()
    {
        var message = MessageProjection.Create(ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), sequenceNumber: 4, deadLetterSource: "orders/subscriptions/billing"));

        Assert.Equal("orders/subscriptions/billing", message.SystemProperties["DeadLetterSource"]);
    }

    [Fact]
    public void Message_without_dead_letter_source_omits_the_property()
    {
        var message = MessageProjection.Create(ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), sequenceNumber: 4));

        Assert.False(message.SystemProperties.ContainsKey("DeadLetterSource"));
    }
}
