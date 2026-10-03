using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.App.Agent;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

public sealed record ProfileAccent(string Name, string ColorHex);

public partial class SettingsWindow : Window
{
    private readonly Func<WorkspacePreferences, Task> _saveAsync;
    private readonly bool _trayAvailable;
    private readonly IAgentAccessStatusSource? _agentStatus;
    private bool _agentSavePending;
    private string? _agentPortError;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _ready;
    private bool _generalSavePending;
    private int _persistencePendingCount;
    private EditableProfile? _selectedEditor;

    public SettingsWindow(WorkspacePreferences preferences, Func<WorkspacePreferences, Task> saveAsync, bool trayAvailable = true,
        IAgentAccessStatusSource? agentStatus = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(saveAsync);

        CurrentPreferences = preferences;
        _saveAsync = saveAsync;
        _trayAvailable = trayAvailable;
        _agentStatus = agentStatus;
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

        AgentClientSelector.ItemsSource = AgentSnippets.Clients.Select(client => client.Name).ToArray();
        AgentClientSelector.SelectedIndex = 0;
        ApplyAgentPreferences(preferences);
        if (_agentStatus is not null)
        {
            _agentStatus.StatusChanged += AgentStatusChanged;
            Closed += (_, _) => _agentStatus.StatusChanged -= AgentStatusChanged;
        }
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
        _selectedEditor.ApplyDraft(connection, color, ConnectionWarning.Text.Trim(), ProfileAgentAccessToggle.IsChecked == true);
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
        ProfileAgentAccessToggle.IsChecked = editor.AllowAgentAccess;
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
            ConnectionWarning.Text.Trim(),
            ProfileAgentAccessToggle.IsChecked == true);

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
            UpdateAgentStatus();
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
        ProfileAgentAccessToggle.IsEnabled = enabled;
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

    private void ApplyAgentPreferences(WorkspacePreferences preferences)
    {
        AgentAccessToggle.IsChecked = preferences.AgentAccessEnabled;
        if (_agentPortError is null) AgentPort.Text = preferences.AgentAccessPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AgentEndpoint.Text = AgentAccessDefaults.Endpoint(preferences.AgentAccessPort);
        AgentToken.Text = preferences.AgentAccessToken.Length == 0 ? string.Empty : new string('•', 16);
        bool hasToken = preferences.AgentAccessToken.Length > 0;
        CopyAgentTokenButton.IsEnabled = hasToken;
        CopyAgentSnippetButton.IsEnabled = hasToken;
        RegenerateAgentTokenButton.IsEnabled = hasToken && !_agentSavePending;
        UpdateAgentSnippet();
        UpdateAgentStatus();
    }

    private async void AgentAccess_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool enabled = AgentAccessToggle.IsChecked == true;
        await SaveAgentAsync(current => current with
        {
            AgentAccessEnabled = enabled,
            AgentAccessToken = current.AgentAccessToken.Length > 0 ? current.AgentAccessToken : AgentAccessDefaults.CreateToken()
        }, enabled ? "Agent access is on." : "Agent access is off.");
    }

    private void AgentPort_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) _ = ApplyAgentPortAsync();
    }

    private void AgentPort_LostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => _ = ApplyAgentPortAsync();

    internal async Task ApplyAgentPortAsync()
    {
        if (!_ready) return;
        if (!int.TryParse(AgentPort.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port)
            || !AgentAccessDefaults.IsValidPort(port))
        {
            // Kept until a valid port is applied; listener status refreshes must not hide it (DEC-013).
            _agentPortError = $"Enter a port between {AgentAccessDefaults.MinimumPort} and {AgentAccessDefaults.MaximumPort}.";
            UpdateAgentStatus();
            return;
        }
        _agentPortError = null;
        if (port == CurrentPreferences.AgentAccessPort)
        {
            UpdateAgentStatus();
            return;
        }
        await SaveAgentAsync(current => current with { AgentAccessPort = port }, $"Agent access port changed to {port}. Update your agents with the new address.");
    }

    private void ChangeAgentPort_Click(object sender, RoutedEventArgs e)
    {
        AgentPort.Focus();
        AgentPort.SelectAll();
    }

    private async void RegenerateAgentToken_Click(object sender, RoutedEventArgs e) =>
        await SaveAgentAsync(current => current with { AgentAccessToken = AgentAccessDefaults.CreateToken() },
            "New token created. Agents using the old token must be set up again.");

    private void CopyAgentEndpoint_Click(object sender, RoutedEventArgs e) =>
        CopyAgentText(AgentAccessDefaults.Endpoint(CurrentPreferences.AgentAccessPort), "Address copied.");

    private void CopyAgentToken_Click(object sender, RoutedEventArgs e) =>
        CopyAgentText(CurrentPreferences.AgentAccessToken, "Access token copied.");

    private void CopyAgentSnippet_Click(object sender, RoutedEventArgs e) =>
        CopyAgentText(AgentSnippets.Build(SelectedAgentClient, AgentAccessDefaults.Endpoint(CurrentPreferences.AgentAccessPort),
            CurrentPreferences.AgentAccessToken).Text, "Copied with the access token included.");

    private void AgentClient_Changed(object sender, SelectionChangedEventArgs e) => UpdateAgentSnippet();

    private AgentClient SelectedAgentClient =>
        AgentSnippets.Clients[Math.Max(0, AgentClientSelector.SelectedIndex)].Client;

    private void UpdateAgentSnippet()
    {
        var snippet = AgentSnippets.Build(SelectedAgentClient, AgentAccessDefaults.Endpoint(CurrentPreferences.AgentAccessPort), AgentSnippets.MaskedToken);
        AgentSnippet.Text = snippet.Text;
        CopyAgentSnippetButton.Content = snippet.CopyLabel;
    }

    private void CopyAgentText(string text, string done)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            AgentsStatus.Text = done;
        }
        catch (ExternalException)
        {
            AgentsStatus.Text = "Clipboard is unavailable. Try copying again.";
        }
    }

    private void AgentStatusChanged(AgentAccessStatus status) => Dispatcher.BeginInvoke(new Action(UpdateAgentStatus));

    private void UpdateAgentStatus()
    {
        var preferences = CurrentPreferences;
        var status = _agentStatus?.Status;
        var activeProfile = Profiles?.FirstOrDefault(profile => profile.Id == preferences.SelectedProfileId);
        bool failed = preferences.AgentAccessEnabled && status?.State == AgentAccessState.Failed && status.Port == preferences.AgentAccessPort;
        string? warning = _agentPortError ?? (failed ? status!.Detail : null);
        AgentPortWarning.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
        AgentPortWarningText.Text = warning ?? string.Empty;

        (string brush, string text) = !preferences.AgentAccessEnabled
                ? ("SecondaryBrush", "Off. Agents cannot connect.")
            : failed
                ? ("DisconnectedHealthBrush", "Not listening.")
            : status is null || status.State != AgentAccessState.Listening || status.Port != preferences.AgentAccessPort
                ? ("SecondaryBrush", "Starting…")
            : activeProfile is { AllowAgentAccess: false }
                ? ("WarningHealthBrush", "Paused: the current connection does not allow agent access.")
            : ("ConnectedHealthBrush", status.LastRequestUtc is { } last
                ? $"Listening · last request {Ago(DateTimeOffset.UtcNow - last)}"
                : "Listening · no requests yet");
        AgentStatusDot.Fill = (System.Windows.Media.Brush)FindResource(brush);
        AgentStatusText.Text = text;
    }

    private static string Ago(TimeSpan elapsed) =>
        elapsed < TimeSpan.FromMinutes(1) ? "just now"
        : elapsed < TimeSpan.FromHours(1) ? $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes == 1 ? "" : "s")} ago"
        : $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours == 1 ? "" : "s")} ago";

    private async Task SaveAgentAsync(Func<WorkspacePreferences, WorkspacePreferences> update, string done)
    {
        if (_agentSavePending) return;
        _agentSavePending = true;
        BeginPersistence();
        SetAgentControlsEnabled(false);
        AgentsStatus.Text = "Saving…";
        try
        {
            await PersistAsync(update);
            AgentsStatus.Text = done;
        }
        catch (Exception exception)
        {
            AgentsStatus.Text = $"Could not save agent settings: {exception.Message}";
        }
        finally
        {
            _agentSavePending = false;
            _ready = false;
            ApplyAgentPreferences(CurrentPreferences);
            _ready = true;
            SetAgentControlsEnabled(true);
            EndPersistence();
        }
    }

    private void SetAgentControlsEnabled(bool enabled)
    {
        AgentAccessToggle.IsEnabled = enabled;
        AgentPort.IsEnabled = enabled;
        RegenerateAgentTokenButton.IsEnabled = enabled && CurrentPreferences.AgentAccessToken.Length > 0;
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
        private bool _allowAgentAccess = true;

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
        public bool AllowAgentAccess => _allowAgentAccess;

        public static EditableProfile From(InvestigationProfile profile)
        {
            var editor = new EditableProfile(profile.Id, profile.Connection.Name, profile.Connection);
            editor._colorHex = profile.ColorHex;
            editor._warningMessage = profile.WarningMessage;
            editor._allowAgentAccess = profile.AllowAgentAccess;
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
            Set(ref _allowAgentAccess, profile.AllowAgentAccess, nameof(AllowAgentAccess));
        }

        public void ApplyDraft(ConnectionProfile connection, string colorHex, string warningMessage, bool allowAgentAccess)
        {
            Set(ref _allowAgentAccess, allowAgentAccess, nameof(AllowAgentAccess));
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
