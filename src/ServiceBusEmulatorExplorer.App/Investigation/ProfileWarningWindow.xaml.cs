using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>The WPF view of <see cref="ProfileWarningViewModel"/>; it only applies the theme, focus and dialog result.</summary>
public partial class ProfileWarningWindow : Window
{
    public ProfileWarningWindow(string profileName, string message, string colorHex,
        string heading = "Connection warning", string continueLabel = "Continue")
    {
        var viewModel = new ProfileWarningViewModel(profileName, message, heading, continueLabel);
        DataContext = viewModel;
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        viewModel.Completed += accepted => DialogResult = accepted;
        Loaded += (_, _) => CancelButton.Focus();
    }
}
