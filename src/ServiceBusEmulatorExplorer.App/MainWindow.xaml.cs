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
}
