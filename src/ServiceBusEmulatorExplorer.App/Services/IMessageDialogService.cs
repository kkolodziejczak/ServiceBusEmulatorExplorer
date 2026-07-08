using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public interface IMessageDialogService
{
    Task<SendMessageCommand?> ShowSendMessageDialogAsync(ServiceBusEntityNode entity);
}
