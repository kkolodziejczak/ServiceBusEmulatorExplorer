using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public interface IEntityManagementDialogService
{
    Task<CreateQueueCommand?> ShowCreateQueueDialogAsync();

    Task<CreateTopicCommand?> ShowCreateTopicDialogAsync();

    Task<CreateSubscriptionCommand?> ShowCreateSubscriptionDialogAsync();

    Task<UpdateQueueCommand?> ShowUpdateQueueDialogAsync(ServiceBusEntityNode entity);

    Task<UpdateTopicCommand?> ShowUpdateTopicDialogAsync(ServiceBusEntityNode entity);

    Task<UpdateSubscriptionCommand?> ShowUpdateSubscriptionDialogAsync(ServiceBusEntityNode entity);

    Task<bool> ConfirmDeleteEntityAsync(ServiceBusEntityNode entity);
}
