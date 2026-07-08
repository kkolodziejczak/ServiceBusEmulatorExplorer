using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public sealed class WpfEntityManagementDialogService : IEntityManagementDialogService
{
    public Task<CreateQueueCommand?> ShowCreateQueueDialogAsync()
    {
        EntityManagementDialogResult? result = ShowEntityDialog(EntityDialogMode.CreateQueue, entity: null);
        return Task.FromResult(result is null
            ? null
            : new CreateQueueCommand(
                result.Name,
                result.LockDuration,
                result.MaxDeliveryCount,
                result.DefaultMessageTimeToLive,
                result.RequiresSession,
                result.RequiresDuplicateDetection));
    }

    public Task<CreateTopicCommand?> ShowCreateTopicDialogAsync()
    {
        EntityManagementDialogResult? result = ShowEntityDialog(EntityDialogMode.CreateTopic, entity: null);
        return Task.FromResult(result is null
            ? null
            : new CreateTopicCommand(
                result.Name,
                result.DefaultMessageTimeToLive,
                result.RequiresDuplicateDetection));
    }

    public Task<CreateSubscriptionCommand?> ShowCreateSubscriptionDialogAsync()
    {
        EntityManagementDialogResult? result = ShowEntityDialog(EntityDialogMode.CreateSubscription, entity: null);
        return Task.FromResult(result is null
            ? null
            : new CreateSubscriptionCommand(
                result.TopicName,
                result.Name,
                result.LockDuration,
                result.MaxDeliveryCount,
                result.DefaultMessageTimeToLive,
                result.RequiresSession));
    }

    public Task<UpdateQueueCommand?> ShowUpdateQueueDialogAsync(ServiceBusEntityNode entity)
    {
        EntityManagementDialogResult? result = ShowEntityDialog(EntityDialogMode.UpdateQueue, entity);
        return Task.FromResult(result is null
            ? null
            : new UpdateQueueCommand(result.Name, result.LockDuration, result.MaxDeliveryCount, result.DefaultMessageTimeToLive));
    }

    public Task<UpdateTopicCommand?> ShowUpdateTopicDialogAsync(ServiceBusEntityNode entity)
    {
        EntityManagementDialogResult? result = ShowEntityDialog(EntityDialogMode.UpdateTopic, entity);
        return Task.FromResult(result is null
            ? null
            : new UpdateTopicCommand(result.Name, result.DefaultMessageTimeToLive));
    }

    public Task<UpdateSubscriptionCommand?> ShowUpdateSubscriptionDialogAsync(ServiceBusEntityNode entity)
    {
        EntityManagementDialogResult? result = ShowEntityDialog(EntityDialogMode.UpdateSubscription, entity);
        return Task.FromResult(result is null
            ? null
            : new UpdateSubscriptionCommand(
                result.TopicName,
                result.Name,
                result.LockDuration,
                result.MaxDeliveryCount,
                result.DefaultMessageTimeToLive));
    }

    public Task<bool> ConfirmDeleteEntityAsync(ServiceBusEntityNode entity)
    {
        var dialog = new DeleteEntityConfirmationDialog(entity)
        {
            Owner = Application.Current.MainWindow
        };

        return Task.FromResult(dialog.ShowDialog() == true);
    }

    private static EntityManagementDialogResult? ShowEntityDialog(
        EntityDialogMode mode,
        ServiceBusEntityNode? entity)
    {
        var dialog = new EntityManagementDialog(mode, entity)
        {
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
