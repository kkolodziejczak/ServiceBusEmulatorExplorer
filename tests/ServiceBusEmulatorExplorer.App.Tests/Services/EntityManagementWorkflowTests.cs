using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests.Services;

public sealed class EntityManagementWorkflowTests
{
    [Fact]
    public async Task Create_methods_send_dialog_commands_to_administration_service()
    {
        var administrationService = new FakeAdministrationService();
        var dialogs = new FakeDialogService
        {
            CreateQueueResult = new CreateQueueCommand("orders"),
            CreateTopicResult = new CreateTopicCommand("events"),
            CreateSubscriptionResult = new CreateSubscriptionCommand("events", "billing")
        };
        var workflow = new EntityManagementWorkflow(administrationService, dialogs);

        EntityManagementOperationResult queueResult = await workflow.CreateQueueAsync(CancellationToken.None);
        EntityManagementOperationResult topicResult = await workflow.CreateTopicAsync(CancellationToken.None);
        EntityManagementOperationResult subscriptionResult = await workflow.CreateSubscriptionAsync(CancellationToken.None);

        Assert.Equal(dialogs.CreateQueueResult, administrationService.CreatedQueue);
        Assert.Equal(dialogs.CreateTopicResult, administrationService.CreatedTopic);
        Assert.Equal(dialogs.CreateSubscriptionResult, administrationService.CreatedSubscription);
        Assert.True(administrationService.LastCancellationTokenCanBeCanceled);
        Assert.Equal("Created queue orders.", queueResult.LogMessage);
        Assert.Equal("Created topic events.", topicResult.LogMessage);
        Assert.Equal("Created subscription events/subscriptions/billing.", subscriptionResult.LogMessage);
    }

    [Fact]
    public async Task UpdateAsync_dispatches_supported_entity_kinds()
    {
        var administrationService = new FakeAdministrationService();
        var dialogs = new FakeDialogService
        {
            UpdateQueueResult = new UpdateQueueCommand("orders"),
            UpdateTopicResult = new UpdateTopicCommand("events"),
            UpdateSubscriptionResult = new UpdateSubscriptionCommand("events", "billing")
        };
        var workflow = new EntityManagementWorkflow(administrationService, dialogs);

        EntityManagementOperationResult queueResult = await workflow.UpdateAsync(CreateEntity(EntityKind.Queue, "orders"), CancellationToken.None);
        EntityManagementOperationResult topicResult = await workflow.UpdateAsync(CreateEntity(EntityKind.Topic, "events"), CancellationToken.None);
        EntityManagementOperationResult subscriptionResult = await workflow.UpdateAsync(CreateEntity(EntityKind.Subscription, "billing", "events"), CancellationToken.None);

        Assert.Equal(dialogs.UpdateQueueResult, administrationService.UpdatedQueue);
        Assert.Equal(dialogs.UpdateTopicResult, administrationService.UpdatedTopic);
        Assert.Equal(dialogs.UpdateSubscriptionResult, administrationService.UpdatedSubscription);
        Assert.Equal("Updated queue orders.", queueResult.LogMessage);
        Assert.Equal("Updated topic events.", topicResult.LogMessage);
        Assert.Equal("Updated subscription events/subscriptions/billing.", subscriptionResult.LogMessage);
    }

    [Fact]
    public async Task DeleteAsync_requires_confirmation_and_dispatches_supported_entity_kinds()
    {
        var administrationService = new FakeAdministrationService();
        var workflow = new EntityManagementWorkflow(
            administrationService,
            new FakeDialogService { ConfirmDeleteResult = true });

        EntityManagementOperationResult queueResult = await workflow.DeleteAsync(CreateEntity(EntityKind.Queue, "orders"), CancellationToken.None);
        EntityManagementOperationResult topicResult = await workflow.DeleteAsync(CreateEntity(EntityKind.Topic, "events"), CancellationToken.None);
        EntityManagementOperationResult subscriptionResult = await workflow.DeleteAsync(CreateEntity(EntityKind.Subscription, "billing", "events"), CancellationToken.None);

        Assert.Equal("orders", administrationService.DeletedQueue);
        Assert.Equal("events", administrationService.DeletedTopic);
        Assert.Equal(("events", "billing"), administrationService.DeletedSubscription);
        Assert.Equal("Deleted queue orders.", queueResult.LogMessage);
        Assert.Equal("Deleted topic events.", topicResult.LogMessage);
        Assert.Equal("Deleted subscription events/subscriptions/billing.", subscriptionResult.LogMessage);
    }

    [Fact]
    public async Task DeleteAsync_returns_no_change_when_confirmation_is_rejected()
    {
        var administrationService = new FakeAdministrationService();
        var workflow = new EntityManagementWorkflow(
            administrationService,
            new FakeDialogService { ConfirmDeleteResult = false });

        EntityManagementOperationResult result = await workflow.DeleteAsync(CreateEntity(EntityKind.Queue, "orders"), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Null(administrationService.DeletedQueue);
    }

    private static ServiceBusEntityNode CreateEntity(
        EntityKind kind,
        string name,
        string? topicName = null)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            ActiveMessageCount: 0,
            DeadLetterMessageCount: 0,
            ScheduledMessageCount: 0,
            Status: "Active",
            CreatedAtUtc: null,
            UpdatedAtUtc: null,
            LockDuration: null,
            MaxDeliveryCount: null,
            DefaultMessageTimeToLive: null,
            RequiresSession: null,
            RequiresDuplicateDetection: null));
    }

    private sealed class FakeAdministrationService : IServiceBusAdministrationService
    {
        public CreateQueueCommand? CreatedQueue { get; private set; }

        public UpdateQueueCommand? UpdatedQueue { get; private set; }

        public string? DeletedQueue { get; private set; }

        public CreateTopicCommand? CreatedTopic { get; private set; }

        public UpdateTopicCommand? UpdatedTopic { get; private set; }

        public string? DeletedTopic { get; private set; }

        public CreateSubscriptionCommand? CreatedSubscription { get; private set; }

        public UpdateSubscriptionCommand? UpdatedSubscription { get; private set; }

        public (string TopicName, string SubscriptionName)? DeletedSubscription { get; private set; }

        public bool LastCancellationTokenCanBeCanceled { get; private set; }

        public Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ServiceBusEntityNode>>([]);
        }

        public Task CreateQueueAsync(CreateQueueCommand command, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            CreatedQueue = command;
            return Task.CompletedTask;
        }

        public Task UpdateQueueAsync(UpdateQueueCommand command, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            UpdatedQueue = command;
            return Task.CompletedTask;
        }

        public Task DeleteQueueAsync(string name, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            DeletedQueue = name;
            return Task.CompletedTask;
        }

        public Task CreateTopicAsync(CreateTopicCommand command, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            CreatedTopic = command;
            return Task.CompletedTask;
        }

        public Task UpdateTopicAsync(UpdateTopicCommand command, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            UpdatedTopic = command;
            return Task.CompletedTask;
        }

        public Task DeleteTopicAsync(string name, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            DeletedTopic = name;
            return Task.CompletedTask;
        }

        public Task CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            CreatedSubscription = command;
            return Task.CompletedTask;
        }

        public Task UpdateSubscriptionAsync(UpdateSubscriptionCommand command, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            UpdatedSubscription = command;
            return Task.CompletedTask;
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            LastCancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            DeletedSubscription = (topicName, subscriptionName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDialogService : IEntityManagementDialogService
    {
        public CreateQueueCommand? CreateQueueResult { get; init; }

        public CreateTopicCommand? CreateTopicResult { get; init; }

        public CreateSubscriptionCommand? CreateSubscriptionResult { get; init; }

        public UpdateQueueCommand? UpdateQueueResult { get; init; }

        public UpdateTopicCommand? UpdateTopicResult { get; init; }

        public UpdateSubscriptionCommand? UpdateSubscriptionResult { get; init; }

        public bool ConfirmDeleteResult { get; init; }

        public Task<CreateQueueCommand?> ShowCreateQueueDialogAsync()
        {
            return Task.FromResult(CreateQueueResult);
        }

        public Task<CreateTopicCommand?> ShowCreateTopicDialogAsync()
        {
            return Task.FromResult(CreateTopicResult);
        }

        public Task<CreateSubscriptionCommand?> ShowCreateSubscriptionDialogAsync()
        {
            return Task.FromResult(CreateSubscriptionResult);
        }

        public Task<UpdateQueueCommand?> ShowUpdateQueueDialogAsync(ServiceBusEntityNode entity)
        {
            return Task.FromResult(UpdateQueueResult);
        }

        public Task<UpdateTopicCommand?> ShowUpdateTopicDialogAsync(ServiceBusEntityNode entity)
        {
            return Task.FromResult(UpdateTopicResult);
        }

        public Task<UpdateSubscriptionCommand?> ShowUpdateSubscriptionDialogAsync(ServiceBusEntityNode entity)
        {
            return Task.FromResult(UpdateSubscriptionResult);
        }

        public Task<bool> ConfirmDeleteEntityAsync(ServiceBusEntityNode entity)
        {
            return Task.FromResult(ConfirmDeleteResult);
        }
    }
}
