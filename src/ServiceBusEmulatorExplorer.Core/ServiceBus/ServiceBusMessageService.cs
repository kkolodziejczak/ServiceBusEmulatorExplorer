using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public sealed class ServiceBusMessageService(IServiceBusClientFactory clientFactory) : IServiceBusMessageService
{
    public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
        EntityAddress address,
        MessageBucket bucket,
        int take,
        long? fromSequenceNumber,
        CancellationToken cancellationToken)
    {
        EnsurePeekAddress(address);
        int boundedTake = Math.Clamp(take, 1, 100);

        await using ServiceBusReceiver receiver = CreateReceiver(address, bucket);
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.PeekMessagesAsync(
            boundedTake,
            fromSequenceNumber,
            cancellationToken);

        return messages
            .Select(MessageProjection.Create)
            .ToList();
    }

    public async Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        SendMessageRequestMapper.EnsureValid(command);

        await using ServiceBusSender sender = clientFactory.RuntimeClient.CreateSender(command.Destination.Name.Trim());
        ServiceBusMessage message = SendMessageRequestMapper.ToServiceBusMessage(command);
        await sender.SendMessageAsync(message, cancellationToken);
    }

    private static void EnsurePeekAddress(EntityAddress address)
    {
        if (address.Kind == EntityKind.Topic)
        {
            throw new ArgumentException("Messages can be inspected on queues and subscriptions, not topics.");
        }

        if (string.IsNullOrWhiteSpace(address.Name))
        {
            throw new ArgumentException("Entity name is required.");
        }

        if (address.Kind == EntityKind.Subscription && string.IsNullOrWhiteSpace(address.TopicName))
        {
            throw new ArgumentException("Topic name is required for subscription messages.");
        }
    }

    private ServiceBusReceiver CreateReceiver(EntityAddress address, MessageBucket bucket)
    {
        var options = new ServiceBusReceiverOptions
        {
            SubQueue = bucket == MessageBucket.DeadLetter ? SubQueue.DeadLetter : SubQueue.None
        };

        return address.Kind == EntityKind.Subscription
            ? clientFactory.RuntimeClient.CreateReceiver(address.TopicName!, address.Name.Trim(), options)
            : clientFactory.RuntimeClient.CreateReceiver(address.Name.Trim(), options);
    }
}
