using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class EntityManagementCommandValidator
{
    public static ValidationResult Validate(CreateQueueCommand command)
    {
        var errors = new List<string>();

        AddNameError(errors, command.Name, "Queue name");
        AddMetadataErrors(errors, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);

        return CreateResult(errors);
    }

    public static ValidationResult Validate(UpdateQueueCommand command)
    {
        var errors = new List<string>();

        AddNameError(errors, command.Name, "Queue name");
        AddMetadataErrors(errors, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);

        return CreateResult(errors);
    }

    public static ValidationResult Validate(CreateTopicCommand command)
    {
        var errors = new List<string>();

        AddNameError(errors, command.Name, "Topic name");
        AddPositiveTimeSpanError(errors, command.DefaultMessageTimeToLive, "Default message TTL");

        return CreateResult(errors);
    }

    public static ValidationResult Validate(UpdateTopicCommand command)
    {
        var errors = new List<string>();

        AddNameError(errors, command.Name, "Topic name");
        AddPositiveTimeSpanError(errors, command.DefaultMessageTimeToLive, "Default message TTL");

        return CreateResult(errors);
    }

    public static ValidationResult Validate(CreateSubscriptionCommand command)
    {
        var errors = new List<string>();

        AddNameError(errors, command.TopicName, "Topic name");
        AddNameError(errors, command.SubscriptionName, "Subscription name");
        AddMetadataErrors(errors, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);

        return CreateResult(errors);
    }

    public static ValidationResult Validate(UpdateSubscriptionCommand command)
    {
        var errors = new List<string>();

        AddNameError(errors, command.TopicName, "Topic name");
        AddNameError(errors, command.SubscriptionName, "Subscription name");
        AddMetadataErrors(errors, command.LockDuration, command.MaxDeliveryCount, command.DefaultMessageTimeToLive);

        return CreateResult(errors);
    }

    private static void AddMetadataErrors(
        List<string> errors,
        TimeSpan? lockDuration,
        int? maxDeliveryCount,
        TimeSpan? defaultMessageTimeToLive)
    {
        AddPositiveTimeSpanError(errors, lockDuration, "Lock duration");
        AddPositiveIntegerError(errors, maxDeliveryCount, "Max delivery count");
        AddPositiveTimeSpanError(errors, defaultMessageTimeToLive, "Default message TTL");
    }

    private static void AddNameError(List<string> errors, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} is required.");
        }
    }

    private static void AddPositiveTimeSpanError(List<string> errors, TimeSpan? value, string fieldName)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"{fieldName} must be greater than zero.");
        }
    }

    private static void AddPositiveIntegerError(List<string> errors, int? value, string fieldName)
    {
        if (value <= 0)
        {
            errors.Add($"{fieldName} must be greater than zero.");
        }
    }

    private static ValidationResult CreateResult(IReadOnlyList<string> errors)
    {
        return errors.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(false, errors);
    }
}
