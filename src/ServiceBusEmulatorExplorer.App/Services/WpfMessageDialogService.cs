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
}
