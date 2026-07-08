using System.Windows;
using ServiceBusEmulatorExplorer.App.ViewModels;

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
}
