using System.Windows;
using System.Windows.Controls;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public sealed class PrototypeConnectionSettings
{
    public List<PrototypeConnectionProfile> Profiles { get; } =
    [
        new("Local emulator", "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=sample-only;UseDevelopmentEmulator=true;", "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=sample-only;UseDevelopmentEmulator=true;"),
        new("Azure development", "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=sample;SharedAccessKey=sample-only;", "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=sample;SharedAccessKey=sample-only;")
    ];
}

public sealed class PrototypeConnectionProfile(string name, string runtime, string administration)
{
    public string Name { get; set; } = name;
    public string RuntimeConnection { get; set; } = runtime;
    public string AdministrationConnection { get; set; } = administration;
}

public partial class PrototypeSettingsWindow : Window
{
    private readonly Action<bool> _setClose;
    private readonly Action<bool> _setNotifications;
    private bool _ready;

    public PrototypeSettingsWindow(bool closeToTray, bool notificationsEnabled, bool canCloseToTray,
        Action<bool> setClose, Action<bool> setNotifications, PrototypeConnectionSettings connections)
    {
        _setClose = setClose;
        _setNotifications = setNotifications;
        InitializeComponent();
        CloseToTrayToggle.IsChecked = closeToTray;
        CloseToTrayToggle.IsEnabled = canCloseToTray;
        TrayUnavailableText.Visibility = canCloseToTray ? Visibility.Collapsed : Visibility.Visible;
        NotificationsToggle.IsChecked = notificationsEnabled;
        ProfilesList.ItemsSource = connections.Profiles;
        GeneralProfiles.ItemsSource = connections.Profiles;
        ProfilesList.SelectedIndex = 0;
        _ready = true;
    }

    private void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) _setClose(CloseToTrayToggle.IsChecked == true);
    }
    private void Notifications_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) _setNotifications(NotificationsToggle.IsChecked == true);
    }
    private void ManageConnections_Click(object sender, RoutedEventArgs e) => SettingsTabs.SelectedItem = ConnectionsTab;

    private void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not PrototypeConnectionProfile profile) return;
        ProfileName.Text = profile.Name;
        RuntimeConnection.Password = profile.RuntimeConnection;
        AdministrationConnection.Password = profile.AdministrationConnection;
        ProfileStatus.Text = string.Empty;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not PrototypeConnectionProfile profile) return;
        if (string.IsNullOrWhiteSpace(ProfileName.Text))
        {
            ProfileStatus.Text = "Enter a profile name.";
            ProfileName.Focus();
            return;
        }

        profile.Name = ProfileName.Text.Trim();
        profile.RuntimeConnection = RuntimeConnection.Password;
        profile.AdministrationConnection = AdministrationConnection.Password;
        ProfilesList.Items.Refresh();
        GeneralProfiles.Items.Refresh();
        ProfileStatus.Text = "Profile saved for this session. No connection was attempted.";
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}



