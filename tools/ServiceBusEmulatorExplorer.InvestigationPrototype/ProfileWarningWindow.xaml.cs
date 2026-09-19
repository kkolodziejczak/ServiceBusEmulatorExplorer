using System.Windows;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class ProfileWarningWindow : Window
{
    public ProfileWarningWindow(string profileName, string message, string colorHex)
    {
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        ProfileNameText.Text = profileName;
        ProfileNameText.ToolTip = profileName;
        WarningMessageText.Text = message;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
