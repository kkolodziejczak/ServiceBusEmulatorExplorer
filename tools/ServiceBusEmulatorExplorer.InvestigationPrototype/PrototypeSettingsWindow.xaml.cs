using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public sealed class PrototypeConnectionSettings
{
    public PrototypeConnectionSettings() => SelectedProfile = Profiles[0];

    public PrototypeConnectionProfile SelectedProfile { get; set; }

    public List<PrototypeConnectionProfile> Profiles { get; } =
    [
        new("Local emulator", "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=sample-only;UseDevelopmentEmulator=true;", "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=sample-only;UseDevelopmentEmulator=true;"),
        new("Azure development", "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=sample;SharedAccessKey=sample-only;", "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=sample;SharedAccessKey=sample-only;") { ColorHex = "#7540BF" }
    ];
}

public sealed class PrototypeConnectionProfile(string name, string runtime, string administration) : INotifyPropertyChanged
{
    private string profileName = name;
    private string colorHex = "#0069FA";
    private string warningMessage = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => profileName;
        set
        {
            if (profileName == value) return;
            profileName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
    public string RuntimeConnection { get; set; } = runtime;
    public string AdministrationConnection { get; set; } = administration;
    public string ColorHex
    {
        get => colorHex;
        set
        {
            if (colorHex == value) return;
            colorHex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorHex)));
        }
    }
    public string WarningMessage
    {
        get => warningMessage;
        set
        {
            if (warningMessage == value) return;
            warningMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WarningMessage)));
        }
    }
}

public sealed record ProfileAccent(string Name, string ColorHex);

public partial class PrototypeSettingsWindow : Window
{
    private readonly Action<bool> _setClose;
    private readonly Action<bool> _setNotifications;
    private readonly PrototypeConnectionSettings _connections;
    private readonly Action<PrototypeConnectionProfile>? _useConnection;
    private readonly Action? _preferencesChanged;
    private bool _ready;

    public PrototypeSettingsWindow(bool closeToTray, bool notificationsEnabled, bool canCloseToTray,
        Action<bool> setClose, Action<bool> setNotifications, PrototypeConnectionSettings connections,
        Action<PrototypeConnectionProfile>? useConnection = null, Action? preferencesChanged = null)
    {
        _setClose = setClose;
        _setNotifications = setNotifications;
        _connections = connections;
        _useConnection = useConnection;
        _preferencesChanged = preferencesChanged;
        InitializeComponent();
        CloseToTrayToggle.IsChecked = closeToTray;
        CloseToTrayToggle.IsEnabled = canCloseToTray;
        TrayUnavailableText.Visibility = canCloseToTray ? Visibility.Collapsed : Visibility.Visible;
        NotificationsToggle.IsChecked = notificationsEnabled;
        ColorPicker.ItemsSource = new ProfileAccent[]
        {
            new("Blue", "#0069FA"), new("Purple", "#7540BF"), new("Teal", "#007F80"),
            new("Orange", "#B85B00"), new("Red", "#C83B3B")
        };
        ProfilesList.ItemsSource = connections.Profiles;
        ConnectionPicker.ItemsSource = connections.Profiles;
        ConnectionPicker.SelectedItem = connections.SelectedProfile;
        ProfilesList.SelectedItem = connections.SelectedProfile;
        UpdateConnectionStatus();
        _ready = true;
    }

    private void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _setClose(CloseToTrayToggle.IsChecked == true);
        _preferencesChanged?.Invoke();
    }
    private void Notifications_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _setNotifications(NotificationsToggle.IsChecked == true);
        _preferencesChanged?.Invoke();
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
        if (_useConnection is null) _connections.SelectedProfile = profile;
        else _useConnection(profile);
        _preferencesChanged?.Invoke();
        UpdateConnectionStatus();
    }

    private void AddConnection_Click(object sender, RoutedEventArgs e)
    {
        var number = 1;
        while (_connections.Profiles.Any(profile => string.Equals(profile.Name, $"Connection {number}", StringComparison.OrdinalIgnoreCase))) number++;
        var profile = new PrototypeConnectionProfile($"Connection {number}", string.Empty, string.Empty);
        _connections.Profiles.Add(profile);
        ProfilesList.Items.Refresh();
        ConnectionPicker.Items.Refresh();
        ProfilesList.SelectedItem = profile;
        ProfilesList.ScrollIntoView(profile);
        ProfileName.Focus();
        ProfileName.SelectAll();
    }

    private void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not PrototypeConnectionProfile profile) return;
        ProfileName.Text = profile.Name;
        RuntimeConnection.Password = profile.RuntimeConnection;
        AdministrationConnection.Password = profile.AdministrationConnection;
        ColorPicker.SelectedValue = profile.ColorHex;
        ConnectionWarning.Text = profile.WarningMessage;
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
        if (ColorPicker.SelectedItem is ProfileAccent accent) profile.ColorHex = accent.ColorHex;
        profile.WarningMessage = ConnectionWarning.Text.Trim();
        ProfilesList.Items.Refresh();
        ConnectionPicker.Items.Refresh();
        if (ReferenceEquals(profile, _connections.SelectedProfile)) _useConnection?.Invoke(profile);
        UpdateConnectionStatus();
        ProfileStatus.Text = "Profile updated. No connection was attempted.";
        _preferencesChanged?.Invoke();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}



