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
        if (DataContext is not ShellViewModel viewModel || sender is not DataGrid grid)
        {
            return;
        }

        if (ReferenceEquals(sender, DeadLetterMessagesGrid))
        {
            IReadOnlyList<ExplorerMessage> selectedMessages = grid
                .SelectedItems
                .OfType<ExplorerMessage>()
                .ToList();
            viewModel.MessageInspection.SelectDeadLetterMessages(selectedMessages);
            return;
        }

        viewModel.MessageInspection.SelectActiveMessage(grid.SelectedItem as ExplorerMessage);
    }
}
