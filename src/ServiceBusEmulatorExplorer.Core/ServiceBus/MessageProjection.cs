using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class MessageProjection
{
    public static ExplorerMessage Create(ServiceBusReceivedMessage message)
    {
        string body = message.Body.ToString();
        return new ExplorerMessage(
            message.MessageId,
            message.SequenceNumber,
            body,
            Messaging.MessageBodyPreview.Create(body, 160),
            message.Body.ToMemory().Length,
            ToUtc(message.EnqueuedTime),
            ToUtc(message.ExpiresAt),
            message.DeliveryCount,
            message.ContentType,
            message.CorrelationId,
            message.SessionId,
            message.Subject,
            CopyProperties(message.ApplicationProperties),
            CreateSystemProperties(message))
        {
            RawBody = BinaryData.FromBytes(message.Body.ToArray())
        };
    }

    private static IReadOnlyDictionary<string, object?> CreateSystemProperties(ServiceBusReceivedMessage message)
    {
        var properties = new Dictionary<string, object?>
        {
            ["MessageId"] = message.MessageId,
            ["SequenceNumber"] = message.SequenceNumber,
            ["EnqueuedTimeUtc"] = ToUtc(message.EnqueuedTime),
            ["ExpiresAtUtc"] = ToUtc(message.ExpiresAt),
            ["DeliveryCount"] = message.DeliveryCount,
            ["ContentType"] = message.ContentType,
            ["CorrelationId"] = message.CorrelationId,
            ["SessionId"] = message.SessionId,
            ["Subject"] = message.Subject,
            ["PartitionKey"] = message.PartitionKey,
            ["TransactionPartitionKey"] = message.TransactionPartitionKey,
            ["ReplyToSessionId"] = message.ReplyToSessionId,
            ["TimeToLive"] = message.TimeToLive,
            ["ReplyTo"] = message.ReplyTo,
            ["To"] = message.To,
            ["DeadLetterReason"] = message.DeadLetterReason,
            ["DeadLetterErrorDescription"] = message.DeadLetterErrorDescription
        };

        if (message.ScheduledEnqueueTime != default)
        {
            properties["ScheduledEnqueueTimeUtc"] = message.ScheduledEnqueueTime.ToUniversalTime();
        }

        return properties;
    }

    private static DateTimeOffset? ToUtc(DateTimeOffset? value)
    {
        return value?.ToUniversalTime();
    }

    private static IReadOnlyDictionary<string, object?> CopyProperties(
        IReadOnlyDictionary<string, object?> properties)
    {
        return properties.ToDictionary(
            property => property.Key,
            property => property.Value,
            StringComparer.Ordinal);
    }
}
