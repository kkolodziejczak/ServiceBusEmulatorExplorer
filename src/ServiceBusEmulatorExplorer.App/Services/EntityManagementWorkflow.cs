using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public sealed class EntityManagementWorkflow(
    IServiceBusAdministrationService administrationService,
    IEntityManagementDialogService entityDialogs) : IEntityManagementWorkflow
{
    public async Task<EntityManagementOperationResult> CreateQueueAsync(CancellationToken cancellationToken)
    {
        CreateQueueCommand? command = await entityDialogs.ShowCreateQueueDialogAsync();
        if (command is null)
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => administrationService.CreateQueueAsync(command, token),
            $"Created queue {command.Name.Trim()}.",
            cancellationToken);
    }

    public async Task<EntityManagementOperationResult> CreateTopicAsync(CancellationToken cancellationToken)
    {
        CreateTopicCommand? command = await entityDialogs.ShowCreateTopicDialogAsync();
        if (command is null)
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => administrationService.CreateTopicAsync(command, token),
            $"Created topic {command.Name.Trim()}.",
            cancellationToken);
    }

    public async Task<EntityManagementOperationResult> CreateSubscriptionAsync(CancellationToken cancellationToken)
    {
        CreateSubscriptionCommand? command = await entityDialogs.ShowCreateSubscriptionDialogAsync();
        if (command is null)
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => administrationService.CreateSubscriptionAsync(command, token),
            $"Created subscription {command.TopicName.Trim()}/subscriptions/{command.SubscriptionName.Trim()}.",
            cancellationToken);
    }

    public async Task<EntityManagementOperationResult> UpdateAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken)
    {
        return entity.Kind switch
        {
            EntityKind.Queue => await UpdateQueueAsync(entity, cancellationToken),
            EntityKind.Topic => await UpdateTopicAsync(entity, cancellationToken),
            EntityKind.Subscription => await UpdateSubscriptionAsync(entity, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported entity kind: {entity.Kind}.")
        };
    }

    private async Task<EntityManagementOperationResult> UpdateQueueAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken)
    {
        UpdateQueueCommand? command = await entityDialogs.ShowUpdateQueueDialogAsync(entity);
        if (command is null)
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => administrationService.UpdateQueueAsync(command, token),
            $"Updated queue {command.Name.Trim()}.",
            cancellationToken);
    }

    private async Task<EntityManagementOperationResult> UpdateTopicAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken)
    {
        UpdateTopicCommand? command = await entityDialogs.ShowUpdateTopicDialogAsync(entity);
        if (command is null)
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => administrationService.UpdateTopicAsync(command, token),
            $"Updated topic {command.Name.Trim()}.",
            cancellationToken);
    }

    private async Task<EntityManagementOperationResult> UpdateSubscriptionAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken)
    {
        UpdateSubscriptionCommand? command = await entityDialogs.ShowUpdateSubscriptionDialogAsync(entity);
        if (command is null)
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => administrationService.UpdateSubscriptionAsync(command, token),
            $"Updated subscription {command.TopicName.Trim()}/subscriptions/{command.SubscriptionName.Trim()}.",
            cancellationToken);
    }

    public async Task<EntityManagementOperationResult> DeleteAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken)
    {
        if (!await entityDialogs.ConfirmDeleteEntityAsync(entity))
        {
            return EntityManagementOperationResult.NoChange;
        }

        return await RunAdministrationOperationAsync(
            token => DeleteEntityAsync(entity, token),
            $"Deleted {entity.Kind.ToString().ToLowerInvariant()} {entity.Metadata.Path}.",
            cancellationToken);
    }

    private static async Task<EntityManagementOperationResult> RunAdministrationOperationAsync(
        Func<CancellationToken, Task> operation,
        string logMessage,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        await operation(timeout.Token);
        return EntityManagementOperationResult.ChangedWithLog(logMessage);
    }

    private async Task DeleteEntityAsync(ServiceBusEntityNode entity, CancellationToken cancellationToken)
    {
        switch (entity.Kind)
        {
            case EntityKind.Queue:
                await administrationService.DeleteQueueAsync(entity.Name, cancellationToken);
                break;
            case EntityKind.Topic:
                await administrationService.DeleteTopicAsync(entity.Name, cancellationToken);
                break;
            case EntityKind.Subscription:
                await administrationService.DeleteSubscriptionAsync(entity.TopicName ?? "", entity.Name, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported entity kind: {entity.Kind}.");
        }
    }
}
