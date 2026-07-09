using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Messaging;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class DeadLetterReplayMessageMapper
{
    public static ServiceBusMessage ToReplayMessage(ReplayRequest request, ServiceBusReceivedMessage original)
    {
        EnsureValid(request);

        return CreateReplayMessage(request, original);
    }

    private static ServiceBusMessage CreateReplayMessage(ReplayRequest request, ServiceBusReceivedMessage original)
    {
        var message = new ServiceBusMessage(CreateBody(request, original))
        {
            MessageId = ReplayMessageIdFactory.Create(request.IdPolicy, original.MessageId),
            ContentType = UseRequestValueOrOriginal(request.ContentType, original.ContentType),
            CorrelationId = UseRequestValueOrOriginal(request.CorrelationId, original.CorrelationId),
            SessionId = UseRequestValueOrOriginal(request.SessionId, original.SessionId),
            Subject = UseRequestValueOrOriginal(request.Subject, original.Subject)
        };

        AddApplicationProperties(message, request.ApplicationProperties ?? original.ApplicationProperties);
        return message;
    }

    private static BinaryData CreateBody(ReplayRequest request, ServiceBusReceivedMessage original)
    {
        return request.EditedBody is null
            ? original.Body
            : BinaryData.FromString(request.EditedBody);
    }

    private static string? UseRequestValueOrOriginal(string? requestValue, string? originalValue)
    {
        return string.IsNullOrWhiteSpace(requestValue) ? originalValue : requestValue.Trim();
    }

    private static void AddApplicationProperties(
        ServiceBusMessage message,
        IReadOnlyDictionary<string, object?> properties)
    {
        foreach (KeyValuePair<string, object?> property in properties)
        {
            if (!string.IsNullOrWhiteSpace(property.Key))
            {
                message.ApplicationProperties[property.Key.Trim()] = property.Value;
            }
        }
    }

    public static void EnsureValid(ReplayRequest request)
    {
        EnsureSourceCanUseDeadLetterSubQueue(request.Source);
        EnsureDestinationCanReceiveReplay(request.Destination);

        if (request.SequenceNumber < 0)
        {
            throw new ArgumentException("DLQ sequence number must be zero or greater.");
        }

        if (request.DeleteOriginal)
        {
            throw new ArgumentException("DLQ replay and DLQ delete are separate MVP operations.");
        }
    }

    public static void EnsureValid(DeleteDeadLetterMessagesRequest request)
    {
        EnsureSourceCanUseDeadLetterSubQueue(request.Source);

        if (request.SequenceNumbers.Count == 0)
        {
            throw new ArgumentException("At least one DLQ sequence number is required.");
        }

        if (request.SequenceNumbers.Any(sequenceNumber => sequenceNumber < 0))
        {
            throw new ArgumentException("DLQ sequence numbers must be zero or greater.");
        }
    }

    private static void EnsureSourceCanUseDeadLetterSubQueue(EntityAddress source)
    {
        if (source.Kind == EntityKind.Topic)
        {
            throw new ArgumentException("DLQ messages can be controlled on queues and subscriptions, not topics.");
        }

        if (string.IsNullOrWhiteSpace(source.Name))
        {
            throw new ArgumentException("Source entity name is required.");
        }

        if (source.Kind == EntityKind.Subscription && string.IsNullOrWhiteSpace(source.TopicName))
        {
            throw new ArgumentException("Topic name is required for subscription DLQ messages.");
        }
    }

    private static void EnsureDestinationCanReceiveReplay(EntityAddress destination)
    {
        if (destination.Kind == EntityKind.Subscription)
        {
            throw new ArgumentException("DLQ replay destinations must be queues or topics.");
        }

        if (string.IsNullOrWhiteSpace(destination.Name))
        {
            throw new ArgumentException("Replay destination name is required.");
        }
    }
}
