using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public interface IEntityManagementWorkflow
{
    Task<EntityManagementOperationResult> CreateQueueAsync(CancellationToken cancellationToken);

    Task<EntityManagementOperationResult> CreateTopicAsync(CancellationToken cancellationToken);

    Task<EntityManagementOperationResult> CreateSubscriptionAsync(CancellationToken cancellationToken);

    Task<EntityManagementOperationResult> UpdateAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken);

    Task<EntityManagementOperationResult> DeleteAsync(
        ServiceBusEntityNode entity,
        CancellationToken cancellationToken);
}
