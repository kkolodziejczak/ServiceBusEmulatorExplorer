using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class SendMessageRequestMapper
{
    public static ServiceBusMessage ToServiceBusMessage(SendMessageCommand command)
    {
        EnsureValid(command);

        var message = new ServiceBusMessage(command.Body)
        {
            ContentType = NullIfWhiteSpace(command.ContentType),
            CorrelationId = NullIfWhiteSpace(command.CorrelationId),
            SessionId = NullIfWhiteSpace(command.SessionId),
            Subject = NullIfWhiteSpace(command.Subject)
        };

        AddApplicationProperties(message, command.ApplicationProperties);
        return message;
    }

    public static void EnsureValid(SendMessageCommand command)
    {
        if (command.Destination.Kind == EntityKind.Subscription)
        {
            throw new ArgumentException("Messages can be sent to queues and topics, not subscriptions.");
        }

        if (string.IsNullOrWhiteSpace(command.Destination.Name))
        {
            throw new ArgumentException("Destination name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Body))
        {
            throw new ArgumentException("Message body is required.");
        }
    }

    private static void AddApplicationProperties(
        ServiceBusMessage message,
        IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null)
        {
            return;
        }

        foreach (KeyValuePair<string, object?> property in properties)
        {
            if (!string.IsNullOrWhiteSpace(property.Key))
            {
                message.ApplicationProperties[property.Key.Trim()] = property.Value;
            }
        }
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
