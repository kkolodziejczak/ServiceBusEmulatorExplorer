using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal sealed class ScenarioProfileStore : IConnectionProfileStore
{
    private static readonly ConnectionProfile Profile = ConnectionProfileDefaults.LocalEmulator with
    {
        Name = "Retail Operations"
    };

    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ConnectionProfile>>([Profile]);
    }

    public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class ScenarioClientFactory : IServiceBusClientFactory
{
    public ServiceBusAdministrationClient AdministrationClient =>
        throw new NotSupportedException("The README scenario does not use an SDK client.");

    public ServiceBusClient RuntimeClient =>
        throw new NotSupportedException("The README scenario does not use an SDK client.");

    public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

internal sealed class ScenarioAdministrationService(IReadOnlyList<ServiceBusEntityNode> entities)
    : IServiceBusAdministrationService
{
    public Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(EntityTreeBuilder.AggregateTopicCounts(entities));
    }

    public Task CreateQueueAsync(CreateQueueCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateQueueAsync(UpdateQueueCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteQueueAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CreateTopicAsync(CreateTopicCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateTopicAsync(UpdateTopicCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteTopicAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateSubscriptionAsync(UpdateSubscriptionCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ScenarioMessageService(IReadOnlyList<ExplorerMessage> messages) : IServiceBusMessageService
{
    public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
        EntityAddress address,
        MessageBucket bucket,
        int take,
        long? fromSequenceNumber,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExplorerMessage> result = bucket == MessageBucket.Active
            && address.Kind == EntityKind.Subscription
            && address.TopicName == "order-events"
            ? messages
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                .Take(take)
                .ToList()
            : [];
        return Task.FromResult(result);
    }

    public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed class UnsupportedDeadLetterReplayService : IDeadLetterReplayService
{
    public Task<ReplayResult> ReplayAsync(ReplayRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The README scenario is read-only.");
    }

    public Task<DeleteDeadLetterMessagesResult> DeleteAsync(
        DeleteDeadLetterMessagesRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The README scenario is read-only.");
    }
}

internal sealed class UnsupportedEntityManagementWorkflow : IEntityManagementWorkflow
{
    public Task<EntityManagementOperationResult> CreateQueueAsync(CancellationToken cancellationToken) => Unsupported();

    public Task<EntityManagementOperationResult> CreateTopicAsync(CancellationToken cancellationToken) => Unsupported();

    public Task<EntityManagementOperationResult> CreateSubscriptionAsync(CancellationToken cancellationToken) => Unsupported();

    public Task<EntityManagementOperationResult> UpdateAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken) => Unsupported();

    public Task<EntityManagementOperationResult> DeleteAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken) => Unsupported();

    private static Task<EntityManagementOperationResult> Unsupported()
    {
        throw new NotSupportedException("The README scenario is read-only.");
    }
}

internal sealed class UnsupportedMessageDialogService : IMessageDialogService
{
    public Task<SendMessageCommand?> ShowSendMessageDialogAsync(ServiceBusEntityNode entity)
    {
        return Task.FromResult<SendMessageCommand?>(null);
    }

    public Task<ReplayMessageEdits?> ShowReplayDeadLetterDialogAsync(
        ServiceBusEntityNode entity,
        ExplorerMessage message)
    {
        return Task.FromResult<ReplayMessageEdits?>(null);
    }

    public Task<bool> ConfirmDeleteDeadLetterMessagesAsync(
        ServiceBusEntityNode entity,
        IReadOnlyList<ExplorerMessage> messages,
        bool visiblePage)
    {
        return Task.FromResult(false);
    }
}

internal sealed class ScenarioClock : IClock
{
    private DateTimeOffset _current = RetailScreenshotData.ScenarioTime;

    public DateTimeOffset UtcNow
    {
        get
        {
            _current = _current.AddSeconds(1);
            return _current;
        }
    }
}
