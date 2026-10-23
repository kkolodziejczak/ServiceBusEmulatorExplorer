using Avalonia.Controls;
using ServiceBusEmulatorExplorer.Avalonia.Styles;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Avalonia.Investigation;

/// <summary>The Avalonia view of <see cref="DeleteMessagesViewModel"/>; it only applies the theme, focus and the dialog result.</summary>
public sealed partial class DeleteMessagesDialog : Window
{
    /// <summary>For the XAML previewer only.</summary>
    public DeleteMessagesDialog() : this(new DeleteMessagesViewModel(SampleDeliveries.DeadLetters(1)), "#0069FA") { }

    public DeleteMessagesDialog(DeleteMessagesViewModel viewModel, string colorHex)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        viewModel.Completed += accepted => Close(accepted);
        Opened += (_, _) => CancelButton.Focus();
    }
}
