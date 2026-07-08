using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class EntityManagementCommandValidatorTests
{
    [Fact]
    public void Validate_create_subscription_requires_topic_and_subscription_names()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new CreateSubscriptionCommand("", ""));

        Assert.False(result.IsValid);
        Assert.Contains("Topic name is required.", result.Errors);
        Assert.Contains("Subscription name is required.", result.Errors);
    }

    [Fact]
    public void Validate_create_subscription_rejects_invalid_supported_metadata()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new CreateSubscriptionCommand(
            "events",
            "billing",
            TimeSpan.Zero,
            MaxDeliveryCount: -1,
            DefaultMessageTimeToLive: TimeSpan.Zero));

        Assert.False(result.IsValid);
        Assert.Contains("Lock duration must be greater than zero.", result.Errors);
        Assert.Contains("Max delivery count must be greater than zero.", result.Errors);
        Assert.Contains("Default message TTL must be greater than zero.", result.Errors);
    }

    [Fact]
    public void Validate_queue_metadata_requires_positive_values_when_supplied()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new CreateQueueCommand(
            "orders",
            TimeSpan.Zero,
            MaxDeliveryCount: 0,
            DefaultMessageTimeToLive: TimeSpan.FromDays(-1)));

        Assert.False(result.IsValid);
        Assert.Contains("Lock duration must be greater than zero.", result.Errors);
        Assert.Contains("Max delivery count must be greater than zero.", result.Errors);
        Assert.Contains("Default message TTL must be greater than zero.", result.Errors);
    }

    [Fact]
    public void Validate_topic_allows_name_with_no_optional_metadata()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new CreateTopicCommand("events"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_update_queue_requires_name_and_positive_metadata()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new UpdateQueueCommand(
            "",
            TimeSpan.Zero,
            MaxDeliveryCount: 0,
            DefaultMessageTimeToLive: TimeSpan.FromSeconds(-1)));

        Assert.False(result.IsValid);
        Assert.Contains("Queue name is required.", result.Errors);
        Assert.Contains("Lock duration must be greater than zero.", result.Errors);
        Assert.Contains("Max delivery count must be greater than zero.", result.Errors);
        Assert.Contains("Default message TTL must be greater than zero.", result.Errors);
    }

    [Fact]
    public void Validate_update_topic_requires_name_and_positive_ttl()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new UpdateTopicCommand(
            "",
            TimeSpan.Zero));

        Assert.False(result.IsValid);
        Assert.Contains("Topic name is required.", result.Errors);
        Assert.Contains("Default message TTL must be greater than zero.", result.Errors);
    }

    [Fact]
    public void Validate_update_subscription_requires_names_and_positive_metadata()
    {
        ValidationResult result = EntityManagementCommandValidator.Validate(new UpdateSubscriptionCommand(
            "",
            "",
            TimeSpan.Zero,
            MaxDeliveryCount: 0,
            DefaultMessageTimeToLive: TimeSpan.Zero));

        Assert.False(result.IsValid);
        Assert.Contains("Topic name is required.", result.Errors);
        Assert.Contains("Subscription name is required.", result.Errors);
        Assert.Contains("Lock duration must be greater than zero.", result.Errors);
        Assert.Contains("Max delivery count must be greater than zero.", result.Errors);
        Assert.Contains("Default message TTL must be greater than zero.", result.Errors);
    }
}
