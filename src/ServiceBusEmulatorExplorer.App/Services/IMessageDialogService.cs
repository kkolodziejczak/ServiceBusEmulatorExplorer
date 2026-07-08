using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public interface IMessageDialogService
{
    Task<SendMessageCommand?> ShowSendMessageDialogAsync(ServiceBusEntityNode entity);

    Task<ReplayMessageEdits?> ShowReplayDeadLetterDialogAsync(
        ServiceBusEntityNode entity,
        ExplorerMessage message);

    Task<bool> ConfirmDeleteDeadLetterMessagesAsync(
        ServiceBusEntityNode entity,
        IReadOnlyList<ExplorerMessage> messages,
        bool visiblePage);
}
