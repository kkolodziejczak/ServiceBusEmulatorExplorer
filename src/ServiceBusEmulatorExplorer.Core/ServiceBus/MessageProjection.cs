using Azure.Core.Amqp;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class MessageProjection
{
    public static ExplorerMessage Create(ServiceBusReceivedMessage message)
    {
        // message.Body throws for value and sequence bodies, and one such message would fail its whole page.
        AmqpMessageBody amqpBody = message.GetRawAmqpMessage().Body;
        BinaryData? rawBody = amqpBody.BodyType == AmqpMessageBodyType.Data ? BinaryData.FromBytes(message.Body.ToArray()) : null;
        string body = rawBody?.ToString() ?? DescribeNonDataBody(amqpBody);
        return new ExplorerMessage(
            message.MessageId,
            message.SequenceNumber,
            body,
            Messaging.MessageBodyPreview.Create(body, 160),
            rawBody?.ToMemory().Length ?? 0,
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
            RawBody = rawBody,
            HasDataBody = rawBody is not null,
            BrokerState = message.State
        };
    }

    private static string DescribeNonDataBody(AmqpMessageBody body)
    {
        if (body.TryGetValue(out object? value)) return Describe(value);
        if (body.TryGetSequence(out var sequence) && sequence is not null)
            return string.Join(Environment.NewLine, sequence.Select(section => "[" + string.Join(", ", section.Select(Describe)) + "]"));
        return string.Empty;
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formatted => formatted.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

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
