using System.Windows;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class DeleteEntityConfirmationDialog : Window
{
    public DeleteEntityConfirmationDialog(ServiceBusEntityNode entity)
    {
        InitializeComponent();
        EntitySummaryText.Text = $"{entity.Kind}: {entity.Metadata.Path}";
    }

    private void ConfirmDeleteCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        DeleteButton.IsEnabled = ConfirmDeleteCheckBox.IsChecked == true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
