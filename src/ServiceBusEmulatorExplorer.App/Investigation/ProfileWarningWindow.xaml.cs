using System.Windows;

using ServiceBusEmulatorExplorer.App.Investigation.Resources;
namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class ProfileWarningWindow : Window
{
    public ProfileWarningWindow(string profileName, string message, string colorHex,
        string heading = "Connection warning", string continueLabel = "Continue")
    {
        InitializeComponent();
        ProfileTheme.Apply(this, colorHex);
        ProfileNameText.Text = profileName;
        ProfileNameText.ToolTip = profileName;
        WarningMessageText.Text = message;
        Title = WarningHeading.Text = heading;
        ContinueLabel.Text = continueLabel;
        System.Windows.Automation.AutomationProperties.SetName(ContinueButton, continueLabel);
        System.Windows.Automation.AutomationProperties.SetName(CancelButton, "Cancel");
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
