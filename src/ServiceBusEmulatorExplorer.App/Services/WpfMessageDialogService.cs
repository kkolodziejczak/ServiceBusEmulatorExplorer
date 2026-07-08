using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public sealed class WpfMessageDialogService : IMessageDialogService
{
    public Task<SendMessageCommand?> ShowSendMessageDialogAsync(ServiceBusEntityNode entity)
    {
        var dialog = new SendMessageDialog(entity)
        {
            Owner = Application.Current.MainWindow
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    public Task<ReplayMessageEdits?> ShowReplayDeadLetterDialogAsync(
        ServiceBusEntityNode entity,
        ExplorerMessage message)
    {
        var dialog = new ReplayDeadLetterDialog(entity, message)
        {
            Owner = Application.Current.MainWindow
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    public Task<bool> ConfirmDeleteDeadLetterMessagesAsync(
        ServiceBusEntityNode entity,
        IReadOnlyList<ExplorerMessage> messages,
        bool visiblePage)
    {
        var dialog = new DeleteDeadLetterMessagesConfirmationDialog(entity, messages, visiblePage)
        {
            Owner = Application.Current.MainWindow
        };

        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
