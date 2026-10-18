using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>The WPF view of <see cref="DeleteMessagesViewModel"/>; it only applies the theme, focus and dialog result.</summary>
public partial class DeleteMessagesWindow : Window
{
    private readonly DeleteMessagesViewModel viewModel;

    public DeleteMessagesWindow(IReadOnlyList<MessageDelivery> targets, string colorHex)
    {
        viewModel = new DeleteMessagesViewModel(targets);
        DataContext = viewModel;
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        viewModel.Completed += accepted => DialogResult = accepted;
        Loaded += (_, _) => CancelButton.Focus();
    }

    public IReadOnlyList<MessageDelivery> Targets => viewModel.Targets;
}
