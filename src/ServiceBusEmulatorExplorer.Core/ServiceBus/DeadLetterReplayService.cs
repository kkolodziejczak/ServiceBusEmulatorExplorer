using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public sealed class DeadLetterReplayService(IServiceBusClientFactory clientFactory) : IDeadLetterReplayService
{
    private const int ReceiveBatchSize = 100;

    public async Task<ReplayResult> ReplayAsync(ReplayRequest request, CancellationToken cancellationToken)
    {
        DeadLetterReplayMessageMapper.EnsureValid(request);

        await using ServiceBusReceiver receiver = CreateDeadLetterReceiver(request.Source);
        ServiceBusReceivedMessage? original = null;
        bool originalAbandoned = false;

        try
        {
            original = await ReceiveSelectedDeadLetterMessageAsync(
                receiver,
                request.SequenceNumber,
                cancellationToken);
            ServiceBusMessage replayMessage = DeadLetterReplayMessageMapper.ToReplayMessage(request, original);

            await using ServiceBusSender sender = clientFactory.RuntimeClient.CreateSender(request.Destination.Name.Trim());
            await sender.SendMessageAsync(replayMessage, cancellationToken);

            await receiver.AbandonMessageAsync(original, cancellationToken: cancellationToken);
            originalAbandoned = true;
            return new ReplayResult(replayMessage.MessageId, OriginalDeleted: false);
        }
        finally
        {
            if (original is not null && !originalAbandoned)
            {
                await AbandonMessageWithoutThrowingAsync(receiver, original);
            }
        }
    }

    public async Task<DeleteDeadLetterMessagesResult> DeleteAsync(
        DeleteDeadLetterMessagesRequest request,
        CancellationToken cancellationToken)
    {
        DeadLetterReplayMessageMapper.EnsureValid(request);

        await using ServiceBusReceiver receiver = CreateDeadLetterReceiver(request.Source);
        IReadOnlyList<ServiceBusReceivedMessage> messages = await ReceiveSelectedDeadLetterMessagesAsync(
            receiver,
            request.SequenceNumbers,
            cancellationToken);

        var pendingMessages = messages.ToList();
        int deletedCount = 0;

        try
        {
            foreach (ServiceBusReceivedMessage message in messages)
            {
                await receiver.CompleteMessageAsync(message, cancellationToken);
                pendingMessages.Remove(message);
                deletedCount++;
            }
        }
        catch (Exception ex)
        {
            await AbandonMessagesWithoutThrowingAsync(receiver, pendingMessages);
            throw new InvalidOperationException(
                $"Deleted {deletedCount} of {messages.Count} selected DLQ message(s) before the operation failed.",
                ex);
        }

        return new DeleteDeadLetterMessagesResult(deletedCount);
    }

    private ServiceBusReceiver CreateDeadLetterReceiver(EntityAddress source)
    {
        var options = new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            SubQueue = SubQueue.DeadLetter
        };

        return source.Kind == EntityKind.Subscription
            ? clientFactory.RuntimeClient.CreateReceiver(source.TopicName!.Trim(), source.Name.Trim(), options)
            : clientFactory.RuntimeClient.CreateReceiver(source.Name.Trim(), options);
    }

    private async Task<ServiceBusReceivedMessage> ReceiveSelectedDeadLetterMessageAsync(
        ServiceBusReceiver receiver,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceBusReceivedMessage> messages = await ReceiveSelectedDeadLetterMessagesAsync(
            receiver,
            [sequenceNumber],
            cancellationToken);

        return messages[0];
    }

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveSelectedDeadLetterMessagesAsync(
        ServiceBusReceiver receiver,
        IReadOnlyList<long> sequenceNumbers,
        CancellationToken cancellationToken)
    {
        var remainingSequenceNumbers = sequenceNumbers.ToHashSet();
        var matchedMessages = new List<ServiceBusReceivedMessage>();
        var unmatchedMessages = new List<ServiceBusReceivedMessage>();
        long highestTargetSequenceNumber = remainingSequenceNumbers.Max();

        while (remainingSequenceNumbers.Count > 0)
        {
            IReadOnlyList<ServiceBusReceivedMessage> receivedMessages = await receiver.ReceiveMessagesAsync(
                ReceiveBatchSize,
                maxWaitTime: TimeSpan.FromSeconds(5),
                cancellationToken);
            if (receivedMessages.Count == 0)
            {
                break;
            }

            KeepMatchesAndAbandonOthers(
                receivedMessages,
                remainingSequenceNumbers,
                matchedMessages,
                unmatchedMessages);

            if (receivedMessages.Max(message => message.SequenceNumber) > highestTargetSequenceNumber)
            {
                break;
            }
        }

        await AbandonMessagesAsync(receiver, unmatchedMessages, cancellationToken);

        if (remainingSequenceNumbers.Count > 0)
        {
            await AbandonMessagesAsync(receiver, matchedMessages, cancellationToken);
            throw new InvalidOperationException(
                $"Could not lock selected DLQ message sequence number(s): {string.Join(", ", remainingSequenceNumbers.Order())}.");
        }

        return matchedMessages;
    }

    private static void KeepMatchesAndAbandonOthers(
        IReadOnlyList<ServiceBusReceivedMessage> receivedMessages,
        ISet<long> remainingSequenceNumbers,
        List<ServiceBusReceivedMessage> matchedMessages,
        List<ServiceBusReceivedMessage> unmatchedMessages)
    {
        foreach (ServiceBusReceivedMessage message in receivedMessages)
        {
            if (remainingSequenceNumbers.Remove(message.SequenceNumber))
            {
                matchedMessages.Add(message);
            }
            else
            {
                unmatchedMessages.Add(message);
            }
        }

    }

    private static async Task AbandonMessagesAsync(
        ServiceBusReceiver receiver,
        IReadOnlyList<ServiceBusReceivedMessage> messages,
        CancellationToken cancellationToken)
    {
        foreach (ServiceBusReceivedMessage message in messages)
        {
            await receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken);
        }
    }

    private static async Task AbandonMessagesWithoutThrowingAsync(
        ServiceBusReceiver receiver,
        IReadOnlyList<ServiceBusReceivedMessage> messages)
    {
        foreach (ServiceBusReceivedMessage message in messages)
        {
            await AbandonMessageWithoutThrowingAsync(receiver, message);
        }
    }

    private static async Task AbandonMessageWithoutThrowingAsync(
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage message)
    {
        try
        {
            await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
        }
        catch
        {
            // Cleanup is best-effort; the lock will expire if abandon cannot be sent.
        }
    }
}
