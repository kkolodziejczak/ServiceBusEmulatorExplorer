using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private void UpdateDeleteSurface()
    {
        var delete = actions.Delete;
        if (DeleteButton.Content is StackPanel panel && panel.Children.OfType<TextBlock>().FirstOrDefault() is { } text) text.Text = delete.Label;
        AutomationProperties.SetName(DeleteButton, delete.Label);
        DeleteButton.IsEnabled = delete.IsEnabled;
        DeleteButton.ToolTip = delete.ToolTip;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) => await actions.DeleteAsync();

    IReadOnlyList<MessageDelivery>? IMessageActionsHost.ConfirmDelete(IReadOnlyList<MessageDelivery> targets)
    {
        var confirmation = new DeleteMessagesWindow(targets, workspace.SelectedProfile.ColorHex) { Owner = this };
        return confirmation.ShowDialog() == true ? confirmation.Targets : null;
    }

    private void ActionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageActionsViewModel.Delete)) UpdateDeleteSurface();
        else if (e.PropertyName is nameof(MessageActionsViewModel.IsDeletePending) or nameof(MessageActionsViewModel.IsReplayPending)
            && !closing) UpdateInspector();
    }
}
