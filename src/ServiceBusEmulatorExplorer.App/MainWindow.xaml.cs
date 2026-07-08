using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void NamespaceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ShellViewModel viewModel && e.NewValue is EntityTreeNodeViewModel node)
        {
            viewModel.SelectEntity(node);
        }
    }

    private void MessageGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel && sender is DataGrid { SelectedItem: ExplorerMessage message })
        {
            viewModel.MessageInspection.SelectMessage(message);
        }
    }
}
