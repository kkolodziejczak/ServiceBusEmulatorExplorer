using System.Windows;
using System.Windows.Controls;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public sealed class PrototypeConnectionSettings
{
    public PrototypeConnectionSettings() => SelectedProfile = Profiles[0];

    public PrototypeConnectionProfile SelectedProfile { get; set; }

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
    private readonly PrototypeConnectionSettings _connections;
    private readonly Action<PrototypeConnectionProfile>? _useConnection;
    private bool _ready;

    public PrototypeSettingsWindow(bool closeToTray, bool notificationsEnabled, bool canCloseToTray,
        Action<bool> setClose, Action<bool> setNotifications, PrototypeConnectionSettings connections,
        Action<PrototypeConnectionProfile>? useConnection = null)
    {
        _setClose = setClose;
        _setNotifications = setNotifications;
        _connections = connections;
        _useConnection = useConnection;
        InitializeComponent();
        CloseToTrayToggle.IsChecked = closeToTray;
        CloseToTrayToggle.IsEnabled = canCloseToTray;
        TrayUnavailableText.Visibility = canCloseToTray ? Visibility.Collapsed : Visibility.Visible;
        NotificationsToggle.IsChecked = notificationsEnabled;
        ProfilesList.ItemsSource = connections.Profiles;
        ConnectionPicker.ItemsSource = connections.Profiles;
        ConnectionPicker.SelectedItem = connections.SelectedProfile;
        ProfilesList.SelectedItem = connections.SelectedProfile;
        UpdateConnectionStatus();
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
    private void ManageConnections_Click(object sender, RoutedEventArgs e)
    {
        ProfilesList.SelectedItem = ConnectionPicker.SelectedItem;
        SettingsTabs.SelectedItem = ConnectionsTab;
    }

    private void ConnectionPicker_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UseConnectionButton.IsEnabled = ConnectionPicker.SelectedItem is PrototypeConnectionProfile;
        UpdateConnectionStatus();
    }

    private void UpdateConnectionStatus()
    {
        ConnectionStatus.Text = $"Current connection: {_connections.SelectedProfile.Name}.";
        if (ConnectionPicker.SelectedItem is PrototypeConnectionProfile selected &&
            !ReferenceEquals(selected, _connections.SelectedProfile))
            ConnectionStatus.Text += " Select Use connection to switch.";
    }

    private void UseConnection_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionPicker.SelectedItem is not PrototypeConnectionProfile profile) return;
        _connections.SelectedProfile = profile;
        _useConnection?.Invoke(profile);
        UpdateConnectionStatus();
    }

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
        ConnectionPicker.Items.Refresh();
        if (ReferenceEquals(profile, _connections.SelectedProfile)) _useConnection?.Invoke(profile);
        UpdateConnectionStatus();
        ProfileStatus.Text = "Profile saved for this session. No connection was attempted.";
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}



