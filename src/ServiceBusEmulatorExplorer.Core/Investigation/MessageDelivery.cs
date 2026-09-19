using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>Identifies one observed delivery within a connection session.</summary>
public sealed record DeliveryIdentity(
    long ConnectionGeneration,
    EntityAddress Source,
    MessageBucket Bucket,
    long SequenceNumber);

/// <summary>A peek observation, retaining the receiving source even in aggregate views.</summary>
public sealed record MessageDelivery
{
    public MessageDelivery(DeliveryIdentity identity, ExplorerMessage message)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(message);
        if (identity.SequenceNumber != message.SequenceNumber)
        {
            throw new ArgumentException("Delivery identity must use the observed message sequence number.", nameof(identity));
        }

        Identity = identity;
        Message = message;
    }

    public DeliveryIdentity Identity { get; }
    public ExplorerMessage Message { get; }
}
