using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

public sealed record ProfileAccent(string Name, string ColorHex);

public partial class SettingsWindow : Window
{
    private readonly Func<WorkspacePreferences, Task> _saveAsync;
    private readonly bool _trayAvailable;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _ready;
    private bool _generalSavePending;
    private int _persistencePendingCount;
    private EditableProfile? _selectedEditor;

    public SettingsWindow(WorkspacePreferences preferences, Func<WorkspacePreferences, Task> saveAsync, bool trayAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(saveAsync);

        CurrentPreferences = preferences;
        _saveAsync = saveAsync;
        _trayAvailable = trayAvailable;
        InitializeComponent();
        CloseToTrayToggle.IsEnabled = trayAvailable;
        if (!trayAvailable) CloseToTrayDescription.Text = "System tray is unavailable in this session.";

        var activeProfile = preferences.Profiles.FirstOrDefault(profile => profile.Id == preferences.SelectedProfileId);
        ProfileTheme.Apply(this, activeProfile?.ColorHex ?? "#0069FA");

        PageSizeChoices = [25, 50, 100, 200];
        SearchTimeBudgetChoices = [10, 30, 60, 120];
        SearchDeliveryBudgetChoices = [1000, 10000, 50000, 100000];
        AuthenticationModes = ["Connection string", "Azure CLI"];
        ColorPicker.ItemsSource = new ProfileAccent[]
        {
            new("Blue", "#0069FA"), new("Purple", "#7540BF"), new("Teal", "#007F80"),
            new("Orange", "#B85B00"), new("Red", "#C83B3B")
        };
        DataContext = this;

        CloseToTrayToggle.IsChecked = preferences.CloseToTray;
        NotificationsToggle.IsChecked = preferences.NotificationsEnabled;
        AutoConnectToggle.IsChecked = preferences.AutoConnectOnSwitch;
        QueuePageSizeSelector.SelectedItem = NormalizePageSize(preferences.QueuePageSize);
        TopicPageSizeSelector.SelectedItem = NormalizePageSize(preferences.TopicPageSize);
        SubscriptionPageSizeSelector.SelectedItem = NormalizePageSize(preferences.SubscriptionPageSize);
        SearchTimeBudgetSelector.SelectedItem = NormalizeSearchTimeBudget(preferences.SearchTimeBudgetSeconds);
        SearchDeliveryBudgetSelector.SelectedItem = NormalizeSearchDeliveryBudget(preferences.SearchDeliveryBudget);

        Profiles = new ObservableCollection<EditableProfile>(preferences.Profiles.Select(EditableProfile.From));
        ProfilesList.ItemsSource = Profiles;
        ProfilesList.SelectedItem = Profiles.FirstOrDefault(profile => profile.Id == preferences.SelectedProfileId) ?? Profiles.FirstOrDefault();
        _ready = true;
        if (ProfilesList.SelectedItem is EditableProfile initialProfile)
        {
            LoadProfile(initialProfile);
        }
    }

    public WorkspacePreferences CurrentPreferences { get; private set; }

    public IReadOnlyList<int> PageSizeChoices { get; }

    public IReadOnlyList<int> SearchTimeBudgetChoices { get; }

    public IReadOnlyList<int> SearchDeliveryBudgetChoices { get; }

    public IReadOnlyList<string> AuthenticationModes { get; }

    public ObservableCollection<EditableProfile> Profiles { get; }

    private async void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) await SaveGeneralAsync(current => current with { CloseToTray = CloseToTrayToggle.IsChecked == true });
    }

    private async void Notifications_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) await SaveGeneralAsync(current => current with { NotificationsEnabled = NotificationsToggle.IsChecked == true });
    }

    private async void AutoConnect_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) await SaveGeneralAsync(current => current with { AutoConnectOnSwitch = AutoConnectToggle.IsChecked == true });
    }

    private async void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || sender is not ComboBox selector || selector.SelectedItem is not int size) return;
        int currentSize = selector == QueuePageSizeSelector ? CurrentPreferences.QueuePageSize
            : selector == TopicPageSizeSelector ? CurrentPreferences.TopicPageSize : CurrentPreferences.SubscriptionPageSize;
        if (size == currentSize) return;

        await SaveGeneralAsync(current => selector == QueuePageSizeSelector
            ? current with { QueuePageSize = size }
            : selector == TopicPageSizeSelector
                ? current with { TopicPageSize = size }
                : current with { SubscriptionPageSize = size });
    }

    private async void SearchBudget_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || sender is not ComboBox selector || selector.SelectedItem is not int value) return;
        int currentValue = selector == SearchTimeBudgetSelector ? CurrentPreferences.SearchTimeBudgetSeconds
            : CurrentPreferences.SearchDeliveryBudget;
        if (value == currentValue) return;

        await SaveGeneralAsync(current => selector == SearchTimeBudgetSelector
            ? current with { SearchTimeBudgetSeconds = value }
            : current with { SearchDeliveryBudget = value });
    }

    private void AddConnection_Click(object sender, RoutedEventArgs e)
    {
        string name = "Connection 1";
        for (var number = 2; Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)); number++)
        {
            name = $"Connection {number}";
        }

        var editor = new EditableProfile(Guid.NewGuid().ToString("N"), name, new ConnectionProfile(name, "", ""));
        Profiles.Add(editor);
        ProfilesList.SelectedItem = editor;
        ProfilesList.ScrollIntoView(editor);
        ProfileName.Focus();
        ProfileName.SelectAll();
    }

    private void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;

        CaptureCurrentEditor();
        if (ProfilesList.SelectedItem is not EditableProfile editor) return;
        LoadProfile(editor);
    }

    private void CaptureCurrentEditor()
    {
        if (_selectedEditor is null) return;

        var connection = new ConnectionProfile(
            ProfileName.Text.Trim(),
            RuntimeConnection.Password,
            AdministrationConnection.Password,
            AuthenticationModeSelector.SelectedIndex == 1
                ? ConnectionAuthenticationMode.AzureCli
                : ConnectionAuthenticationMode.ConnectionString,
            FullyQualifiedNamespace.Text.Trim());
        string color = ColorPicker.SelectedItem is ProfileAccent accent
            ? accent.ColorHex
            : _selectedEditor.ColorHex;
        _selectedEditor.ApplyDraft(connection, color, ConnectionWarning.Text.Trim());
    }

    private void LoadProfile(EditableProfile editor)
    {
        _selectedEditor = editor;
        ProfileName.Text = editor.Name;
        AuthenticationModeSelector.SelectedIndex = editor.AuthenticationMode == ConnectionAuthenticationMode.AzureCli ? 1 : 0;
        RuntimeConnection.Password = editor.RuntimeConnectionString;
        AdministrationConnection.Password = editor.AdministrationConnectionString;
        FullyQualifiedNamespace.Text = editor.FullyQualifiedNamespace;
        ColorPicker.SelectedValue = editor.ColorHex;
        ConnectionWarning.Text = editor.WarningMessage;
        ProfileStatus.Text = string.Empty;
        UpdateAuthenticationFields();
    }

    private void AuthenticationMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) UpdateAuthenticationFields();
    }

    private void UpdateAuthenticationFields()
    {
        bool connectionStrings = AuthenticationModeSelector.SelectedIndex != 1;
        ConnectionStringsPanel.Visibility = connectionStrings ? Visibility.Visible : Visibility.Collapsed;
        AzureCliPanel.Visibility = connectionStrings ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConnectionString_Changed(object sender, RoutedEventArgs e)
    {
        if (CopyRuntimeConnectionButton is not null) CopyRuntimeConnectionButton.IsEnabled = HasConnectionString(RuntimeConnection);
        if (CopyAdministrationConnectionButton is not null) CopyAdministrationConnectionButton.IsEnabled = HasConnectionString(AdministrationConnection);
    }

    private static bool HasConnectionString(PasswordBox field)
    {
        using var password = field.SecurePassword;
        return password.Length > 0;
    }

    private void CopyRuntimeConnection_Click(object sender, RoutedEventArgs e) => CopyConnectionString(RuntimeConnection, "Runtime");

    private void CopyAdministrationConnection_Click(object sender, RoutedEventArgs e) => CopyConnectionString(AdministrationConnection, "Administration");

    private void CopyConnectionString(PasswordBox field, string label)
    {
        if (!HasConnectionString(field)) return;
        try
        {
            Clipboard.SetText(field.Password);
            ProfileStatus.Text = $"{label} connection string copied.";
        }
        catch (ExternalException)
        {
            ProfileStatus.Text = "Clipboard is unavailable. Try copying again.";
        }
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEditor is null || !SaveProfileButton.IsEnabled) return;

        var editor = _selectedEditor;
        string name = ProfileName.Text.Trim();
        var authenticationMode = AuthenticationModeSelector.SelectedIndex == 1
            ? ConnectionAuthenticationMode.AzureCli
            : ConnectionAuthenticationMode.ConnectionString;
        var connection = new ConnectionProfile(
            name,
            RuntimeConnection.Password,
            AdministrationConnection.Password,
            authenticationMode,
            FullyQualifiedNamespace.Text.Trim());
        var validation = ConnectionProfileValidator.Validate(connection);
        if (!validation.IsValid)
        {
            ProfileStatus.Text = string.Join(" ", validation.Errors);
            ProfileName.Focus();
            return;
        }

        var savedProfile = new InvestigationProfile(
            editor.Id,
            connection,
            ColorPicker.SelectedItem is ProfileAccent accent ? accent.ColorHex : editor.ColorHex,
            ConnectionWarning.Text.Trim());

        try
        {
            BeginPersistence();
            SetProfileEditorEnabled(false);
            SaveProfileButton.IsEnabled = false;
            await PersistAsync(current => current with
            {
                Profiles = current.Profiles.Any(profile => profile.Id == savedProfile.Id)
                    ? current.Profiles.Select(profile => profile.Id == savedProfile.Id ? savedProfile : profile).ToArray()
                    : current.Profiles.Append(savedProfile).ToArray()
            });
            editor.Apply(savedProfile);
            ProfilesList.Items.Refresh();
            if (ReferenceEquals(_selectedEditor, editor))
            {
                ProfileStatus.Text = "Profile saved. No connection was attempted.";
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_selectedEditor, editor))
            {
                ProfileStatus.Text = $"Could not save profile: {exception.Message}";
            }
        }
        finally
        {
            SetProfileEditorEnabled(true);
            SaveProfileButton.IsEnabled = true;
            EndPersistence();
        }
    }

    private async Task SaveGeneralAsync(Func<WorkspacePreferences, WorkspacePreferences> update)
    {
        if (_generalSavePending) return;
        _generalSavePending = true;
        BeginPersistence();
        SetGeneralControlsEnabled(false);
        GeneralStatus.Text = "Saving…";
        try
        {
            await PersistAsync(update);
            GeneralStatus.Text = "General preferences saved.";
        }
        catch (Exception exception)
        {
            ApplyGeneralPreferences(CurrentPreferences);
            GeneralStatus.Text = $"Could not save preferences: {exception.Message}";
        }
        finally
        {
            _generalSavePending = false;
            SetGeneralControlsEnabled(true);
            EndPersistence();
        }
    }

    private void SetProfileEditorEnabled(bool enabled)
    {
        ProfilesList.IsEnabled = enabled;
        AddConnectionButton.IsEnabled = enabled;
        ProfileName.IsEnabled = enabled;
        AuthenticationModeSelector.IsEnabled = enabled;
        ConnectionStringsPanel.IsEnabled = enabled;
        AzureCliPanel.IsEnabled = enabled;
        ColorPicker.IsEnabled = enabled;
        ConnectionWarning.IsEnabled = enabled;
    }

    private void BeginPersistence()
    {
        _persistencePendingCount++;
        SaveProfileButton.IsEnabled = false;
        DoneButton.IsEnabled = false;
    }

    private void EndPersistence()
    {
        _persistencePendingCount = Math.Max(0, _persistencePendingCount - 1);
        if (_persistencePendingCount == 0)
        {
            SaveProfileButton.IsEnabled = true;
            DoneButton.IsEnabled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_persistencePendingCount == 0) return;

        e.Cancel = true;
        GeneralStatus.Text = "Saving preferences. Wait for the save to finish before closing.";
    }

    private void SetGeneralControlsEnabled(bool enabled)
    {
        CloseToTrayToggle.IsEnabled = enabled && _trayAvailable;
        NotificationsToggle.IsEnabled = enabled;
        AutoConnectToggle.IsEnabled = enabled;
        QueuePageSizeSelector.IsEnabled = enabled;
        TopicPageSizeSelector.IsEnabled = enabled;
        SubscriptionPageSizeSelector.IsEnabled = enabled;
        SearchTimeBudgetSelector.IsEnabled = enabled;
        SearchDeliveryBudgetSelector.IsEnabled = enabled;
    }

    private void ApplyGeneralPreferences(WorkspacePreferences preferences)
    {
        CloseToTrayToggle.IsChecked = preferences.CloseToTray;
        NotificationsToggle.IsChecked = preferences.NotificationsEnabled;
        AutoConnectToggle.IsChecked = preferences.AutoConnectOnSwitch;
        QueuePageSizeSelector.SelectedItem = NormalizePageSize(preferences.QueuePageSize);
        TopicPageSizeSelector.SelectedItem = NormalizePageSize(preferences.TopicPageSize);
        SubscriptionPageSizeSelector.SelectedItem = NormalizePageSize(preferences.SubscriptionPageSize);
        SearchTimeBudgetSelector.SelectedItem = NormalizeSearchTimeBudget(preferences.SearchTimeBudgetSeconds);
        SearchDeliveryBudgetSelector.SelectedItem = NormalizeSearchDeliveryBudget(preferences.SearchDeliveryBudget);
    }

    private async Task PersistAsync(Func<WorkspacePreferences, WorkspacePreferences> update)
    {
        await _saveGate.WaitAsync();
        try
        {
            WorkspacePreferences next = update(CurrentPreferences);
            await _saveAsync(next);
            CurrentPreferences = next;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static int NormalizePageSize(int value) => value is 25 or 50 or 100 or 200 ? value : 50;

    private static int NormalizeSearchTimeBudget(int value) => value is 10 or 30 or 60 or 120 ? value : 30;

    private static int NormalizeSearchDeliveryBudget(int value) => value is 1000 or 10000 or 50000 or 100000 ? value : 10000;

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class EditableProfile : INotifyPropertyChanged
    {
        private string _name;
        private string _runtimeConnectionString;
        private string _administrationConnectionString;
        private ConnectionAuthenticationMode _authenticationMode;
        private string _fullyQualifiedNamespace;
        private string _colorHex;
        private string _warningMessage;

        public EditableProfile(string id, string name, ConnectionProfile connection)
        {
            Id = id;
            _name = name;
            _runtimeConnectionString = connection.RuntimeConnectionString;
            _administrationConnectionString = connection.AdministrationConnectionString;
            _authenticationMode = connection.AuthenticationMode;
            _fullyQualifiedNamespace = connection.FullyQualifiedNamespace;
            _colorHex = "#0069FA";
            _warningMessage = string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public string Name => _name;
        public string RuntimeConnectionString => _runtimeConnectionString;
        public string AdministrationConnectionString => _administrationConnectionString;
        public ConnectionAuthenticationMode AuthenticationMode => _authenticationMode;
        public string FullyQualifiedNamespace => _fullyQualifiedNamespace;
        public string ColorHex => _colorHex;
        public string WarningMessage => _warningMessage;

        public static EditableProfile From(InvestigationProfile profile)
        {
            var editor = new EditableProfile(profile.Id, profile.Connection.Name, profile.Connection);
            editor._colorHex = profile.ColorHex;
            editor._warningMessage = profile.WarningMessage;
            return editor;
        }

        public void Apply(InvestigationProfile profile)
        {
            Set(ref _name, profile.Connection.Name, nameof(Name));
            Set(ref _runtimeConnectionString, profile.Connection.RuntimeConnectionString, nameof(RuntimeConnectionString));
            Set(ref _administrationConnectionString, profile.Connection.AdministrationConnectionString, nameof(AdministrationConnectionString));
            Set(ref _authenticationMode, profile.Connection.AuthenticationMode, nameof(AuthenticationMode));
            Set(ref _fullyQualifiedNamespace, profile.Connection.FullyQualifiedNamespace, nameof(FullyQualifiedNamespace));
            Set(ref _colorHex, profile.ColorHex, nameof(ColorHex));
            Set(ref _warningMessage, profile.WarningMessage, nameof(WarningMessage));
        }

        public void ApplyDraft(ConnectionProfile connection, string colorHex, string warningMessage)
        {
            Set(ref _name, connection.Name, nameof(Name));
            Set(ref _runtimeConnectionString, connection.RuntimeConnectionString, nameof(RuntimeConnectionString));
            Set(ref _administrationConnectionString, connection.AdministrationConnectionString, nameof(AdministrationConnectionString));
            Set(ref _authenticationMode, connection.AuthenticationMode, nameof(AuthenticationMode));
            Set(ref _fullyQualifiedNamespace, connection.FullyQualifiedNamespace, nameof(FullyQualifiedNamespace));
            Set(ref _colorHex, colorHex, nameof(ColorHex));
            Set(ref _warningMessage, warningMessage, nameof(WarningMessage));
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
