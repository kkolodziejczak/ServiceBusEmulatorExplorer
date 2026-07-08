using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class EntityManagementRequestMapper
{
    public static CreateQueueOptions ToCreateQueueOptions(CreateQueueCommand command)
    {
        var options = new CreateQueueOptions(command.Name.Trim())
        {
            RequiresSession = command.RequiresSession,
            RequiresDuplicateDetection = command.RequiresDuplicateDetection
        };

        ApplyQueueMetadata(options, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);

        return options;
    }

    public static void ApplyQueueUpdate(QueueProperties properties, UpdateQueueCommand command)
    {
        ApplyQueueMetadata(properties, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);
    }

    public static CreateTopicOptions ToCreateTopicOptions(CreateTopicCommand command)
    {
        var options = new CreateTopicOptions(command.Name.Trim())
        {
            RequiresDuplicateDetection = command.RequiresDuplicateDetection
        };

        if (command.DefaultMessageTimeToLive is not null)
        {
            options.DefaultMessageTimeToLive = command.DefaultMessageTimeToLive.Value;
        }

        return options;
    }

    public static void ApplyTopicUpdate(TopicProperties properties, UpdateTopicCommand command)
    {
        if (command.DefaultMessageTimeToLive is not null)
        {
            properties.DefaultMessageTimeToLive = command.DefaultMessageTimeToLive.Value;
        }
    }

    public static CreateSubscriptionOptions ToCreateSubscriptionOptions(CreateSubscriptionCommand command)
    {
        var options = new CreateSubscriptionOptions(command.TopicName.Trim(), command.SubscriptionName.Trim())
        {
            RequiresSession = command.RequiresSession
        };

        ApplySubscriptionMetadata(options, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);

        return options;
    }

    public static void ApplySubscriptionUpdate(SubscriptionProperties properties, UpdateSubscriptionCommand command)
    {
        ApplySubscriptionMetadata(properties, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);
    }

    private static void ApplyQueueMetadata(
        CreateQueueOptions options,
        TimeSpan? lockDuration,
        int? maxDeliveryCount,
        TimeSpan? defaultMessageTimeToLive)
    {
        if (lockDuration is not null)
        {
            options.LockDuration = lockDuration.Value;
        }

        if (maxDeliveryCount is not null)
        {
            options.MaxDeliveryCount = maxDeliveryCount.Value;
        }

        if (defaultMessageTimeToLive is not null)
        {
            options.DefaultMessageTimeToLive = defaultMessageTimeToLive.Value;
        }
    }

    private static void ApplyQueueMetadata(
        QueueProperties properties,
        TimeSpan? lockDuration,
        int? maxDeliveryCount,
        TimeSpan? defaultMessageTimeToLive)
    {
        if (lockDuration is not null)
        {
            properties.LockDuration = lockDuration.Value;
        }

        if (maxDeliveryCount is not null)
        {
            properties.MaxDeliveryCount = maxDeliveryCount.Value;
        }

        if (defaultMessageTimeToLive is not null)
        {
            properties.DefaultMessageTimeToLive = defaultMessageTimeToLive.Value;
        }
    }

    private static void ApplySubscriptionMetadata(
        CreateSubscriptionOptions options,
        TimeSpan? lockDuration,
        int? maxDeliveryCount,
        TimeSpan? defaultMessageTimeToLive)
    {
        if (lockDuration is not null)
        {
            options.LockDuration = lockDuration.Value;
        }

        if (maxDeliveryCount is not null)
        {
            options.MaxDeliveryCount = maxDeliveryCount.Value;
        }

        if (defaultMessageTimeToLive is not null)
        {
            options.DefaultMessageTimeToLive = defaultMessageTimeToLive.Value;
        }
    }

    private static void ApplySubscriptionMetadata(
        SubscriptionProperties properties,
        TimeSpan? lockDuration,
        int? maxDeliveryCount,
        TimeSpan? defaultMessageTimeToLive)
    {
        if (lockDuration is not null)
        {
            properties.LockDuration = lockDuration.Value;
        }

        if (maxDeliveryCount is not null)
        {
            properties.MaxDeliveryCount = maxDeliveryCount.Value;
        }

        if (defaultMessageTimeToLive is not null)
        {
            properties.DefaultMessageTimeToLive = defaultMessageTimeToLive.Value;
        }
    }
}
