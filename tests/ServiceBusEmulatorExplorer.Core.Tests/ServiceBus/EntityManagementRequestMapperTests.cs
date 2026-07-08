using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class EntityManagementRequestMapperTests
{
    [Fact]
    public void ToCreateQueueOptions_maps_name_and_supported_metadata()
    {
        CreateQueueOptions options = EntityManagementRequestMapper.ToCreateQueueOptions(new CreateQueueCommand(
            " orders ",
            TimeSpan.FromSeconds(45),
            MaxDeliveryCount: 7,
            DefaultMessageTimeToLive: TimeSpan.FromDays(3),
            RequiresSession: true,
            RequiresDuplicateDetection: true));

        Assert.Equal("orders", options.Name);
        Assert.Equal(TimeSpan.FromSeconds(45), options.LockDuration);
        Assert.Equal(7, options.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromDays(3), options.DefaultMessageTimeToLive);
        Assert.True(options.RequiresSession);
        Assert.True(options.RequiresDuplicateDetection);
    }

    [Fact]
    public void ToCreateTopicOptions_maps_name_and_supported_metadata()
    {
        CreateTopicOptions options = EntityManagementRequestMapper.ToCreateTopicOptions(new CreateTopicCommand(
            " events ",
            TimeSpan.FromDays(5),
            RequiresDuplicateDetection: true));

        Assert.Equal("events", options.Name);
        Assert.Equal(TimeSpan.FromDays(5), options.DefaultMessageTimeToLive);
        Assert.True(options.RequiresDuplicateDetection);
    }

    [Fact]
    public void ToCreateSubscriptionOptions_maps_topic_subscription_and_supported_metadata()
    {
        CreateSubscriptionOptions options = EntityManagementRequestMapper.ToCreateSubscriptionOptions(new CreateSubscriptionCommand(
            " events ",
            " billing ",
            TimeSpan.FromSeconds(30),
            MaxDeliveryCount: 4,
            DefaultMessageTimeToLive: TimeSpan.FromDays(2),
            RequiresSession: true));

        Assert.Equal("events", options.TopicName);
        Assert.Equal("billing", options.SubscriptionName);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LockDuration);
        Assert.Equal(4, options.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromDays(2), options.DefaultMessageTimeToLive);
        Assert.True(options.RequiresSession);
    }

    [Fact]
    public void ApplyQueueUpdate_maps_supported_metadata()
    {
        QueueProperties properties = ServiceBusModelFactory.QueueProperties(
            "orders",
            lockDuration: TimeSpan.FromSeconds(30),
            defaultMessageTimeToLive: TimeSpan.FromDays(1),
            autoDeleteOnIdle: TimeSpan.FromDays(30),
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10),
            maxDeliveryCount: 1,
            status: EntityStatus.Active,
            userMetadata: "");

        EntityManagementRequestMapper.ApplyQueueUpdate(properties, new UpdateQueueCommand(
            "orders",
            TimeSpan.FromSeconds(35),
            MaxDeliveryCount: 6,
            DefaultMessageTimeToLive: TimeSpan.FromDays(4)));

        Assert.Equal(TimeSpan.FromSeconds(35), properties.LockDuration);
        Assert.Equal(6, properties.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromDays(4), properties.DefaultMessageTimeToLive);
    }

    [Fact]
    public void ApplyTopicUpdate_maps_supported_metadata()
    {
        TopicProperties properties = ServiceBusModelFactory.TopicProperties(
            "events",
            defaultMessageTimeToLive: TimeSpan.FromDays(1),
            autoDeleteOnIdle: TimeSpan.FromDays(30),
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10),
            status: EntityStatus.Active);

        EntityManagementRequestMapper.ApplyTopicUpdate(properties, new UpdateTopicCommand(
            "events",
            TimeSpan.FromDays(6)));

        Assert.Equal(TimeSpan.FromDays(6), properties.DefaultMessageTimeToLive);
    }

    [Fact]
    public void ApplySubscriptionUpdate_maps_supported_metadata()
    {
        SubscriptionProperties properties = ServiceBusModelFactory.SubscriptionProperties(
            "events",
            "billing",
            lockDuration: TimeSpan.FromSeconds(30),
            defaultMessageTimeToLive: TimeSpan.FromDays(1),
            autoDeleteOnIdle: TimeSpan.FromDays(30),
            maxDeliveryCount: 1,
            status: EntityStatus.Active,
            userMetadata: "");

        EntityManagementRequestMapper.ApplySubscriptionUpdate(properties, new UpdateSubscriptionCommand(
            "events",
            "billing",
            TimeSpan.FromSeconds(50),
            MaxDeliveryCount: 9,
            DefaultMessageTimeToLive: TimeSpan.FromDays(8)));

        Assert.Equal(TimeSpan.FromSeconds(50), properties.LockDuration);
        Assert.Equal(9, properties.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromDays(8), properties.DefaultMessageTimeToLive);
    }
}
