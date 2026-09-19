using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationDeliveryTests
{
    [Fact]
    public void Observation_rejects_sequence_identity_from_another_message()
    {
        var message = MessageProjection.Create(ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{}"), sequenceNumber: 12));
        var identity = new DeliveryIdentity(1, new EntityAddress(EntityKind.Queue, "orders"), MessageBucket.Active, 13);

        Assert.Throws<ArgumentException>(() => new MessageDelivery(identity, message));
        Assert.Throws<ArgumentNullException>(() => new MessageDelivery(null!, message));
        Assert.Throws<ArgumentNullException>(() => new MessageDelivery(identity, null!));
    }

    [Fact]
    public void Same_ids_and_sequence_in_different_sources_remain_distinct_deliveries()
    {
        var message = MessageProjection.Create(ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{}"), messageId: "same", sequenceNumber: 12));
        var first = new MessageDelivery(new DeliveryIdentity(1,
            new EntityAddress(EntityKind.Subscription, "billing", "orders"), MessageBucket.Active, 12), message);
        var second = new MessageDelivery(new DeliveryIdentity(1,
            new EntityAddress(EntityKind.Subscription, "audit", "orders"), MessageBucket.Active, 12), message);

        Assert.Equal(2, new[] { first.Identity, second.Identity }.Distinct().Count());
        Assert.NotEqual(first.Identity, first.Identity with { ConnectionGeneration = 2 });
        Assert.NotEqual(first.Identity, first.Identity with { Bucket = MessageBucket.DeadLetter });
    }
}
