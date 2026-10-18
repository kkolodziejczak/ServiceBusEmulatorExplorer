using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// The Connections tab of Settings: one editor for the selected profile, a draft kept per profile while switching,
/// validation before saving, and exact copies of the typed connection strings (DEC-053).
/// </summary>
public sealed class ConnectionProfilesViewModel : ObservableObject
{
    public const string ClipboardUnavailable = "Clipboard is unavailable. Try copying again.";

    private const int AzureCliIndex = 1;

    private readonly SettingsPersistence persistence;
    private readonly IConnectionSecrets secrets;
    private readonly Action<string> copyText;
    private readonly ProfileDraft? initialProfile;
    private ProfileDraft? selectedProfile;
    private ProfileDraft? editing;
    private string profileName = string.Empty;
    private int authenticationModeIndex;
    private string fullyQualifiedNamespace = string.Empty;
    private string? colorHex;
    private string warningMessage = string.Empty;
    private string status = string.Empty;
    private bool isEditable = true;
    private bool hasRuntimeConnection;
    private bool hasAdministrationConnection;
    private bool started;

    public ConnectionProfilesViewModel(SettingsPersistence persistence, IConnectionSecrets secrets, Action<string> copyText)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(copyText);
        this.persistence = persistence;
        this.secrets = secrets;
        this.copyText = copyText;
        Profiles = new ObservableCollection<ProfileDraft>(persistence.Saved.Profiles.Select(ProfileDraft.From));
        initialProfile = Profiles.FirstOrDefault(profile => profile.Id == persistence.Saved.SelectedProfileId) ?? Profiles.FirstOrDefault();
        AddConnectionCommand = new RelayCommand(AddConnection, () => IsEditable);
        SaveProfileCommand = new RelayCommand(() => _ = SaveProfileAsync(), () => !persistence.IsSaving);
        CopyRuntimeConnectionCommand = new RelayCommand(() => Copy(secrets.Runtime, "Runtime"), () => HasRuntimeConnection);
        CopyAdministrationConnectionCommand = new RelayCommand(() => Copy(secrets.Administration, "Administration"), () => HasAdministrationConnection);
        persistence.PropertyChanged += (_, _) => SaveProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Raised when a profile is shown in the editor, so the view can fill its password fields.</summary>
    public event Action<ProfileDraft>? ProfileLoaded;

    /// <summary>Raised when a new connection is added and selected, so the view can scroll it into view.</summary>
    public event Action<ProfileDraft>? ProfileAdded;

    /// <summary>Raised when the profile name needs attention; <c>true</c> also selects its text.</summary>
    public event Action<bool>? NameFocusRequested;

    public ObservableCollection<ProfileDraft> Profiles { get; }

    public IReadOnlyList<string> AuthenticationModes { get; } = ["Connection string", "Azure CLI"];

    public ProfileDraft? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value) || !started) return;
            CaptureEditor();
            if (value is not null) Load(value);
        }
    }

    public string ProfileName
    {
        get => profileName;
        set => SetProperty(ref profileName, value ?? string.Empty);
    }

    /// <summary>0 for connection strings, 1 for Azure CLI, as listed in <see cref="AuthenticationModes"/>.</summary>
    public int AuthenticationModeIndex
    {
        get => authenticationModeIndex;
        set
        {
            if (!SetProperty(ref authenticationModeIndex, value)) return;
            OnPropertyChanged(nameof(UsesConnectionStrings));
            OnPropertyChanged(nameof(UsesAzureCli));
        }
    }

    public bool UsesConnectionStrings => !UsesAzureCli;

    public bool UsesAzureCli => AuthenticationModeIndex == AzureCliIndex;

    public string FullyQualifiedNamespace
    {
        get => fullyQualifiedNamespace;
        set => SetProperty(ref fullyQualifiedNamespace, value ?? string.Empty);
    }

    /// <summary>The chosen accent; null when the profile's colour is not one of the offered ones.</summary>
    public string? ColorHex
    {
        get => colorHex;
        set => SetProperty(ref colorHex, value);
    }

    public string WarningMessage
    {
        get => warningMessage;
        set => SetProperty(ref warningMessage, value ?? string.Empty);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <summary>False while a profile save runs, so the editor cannot change what is being saved.</summary>
    public bool IsEditable
    {
        get => isEditable;
        private set
        {
            if (SetProperty(ref isEditable, value)) AddConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Whether the runtime field holds anything; the view reports it without handing over the text.</summary>
    public bool HasRuntimeConnection
    {
        get => hasRuntimeConnection;
        set
        {
            if (SetProperty(ref hasRuntimeConnection, value)) CopyRuntimeConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasAdministrationConnection
    {
        get => hasAdministrationConnection;
        set
        {
            if (SetProperty(ref hasAdministrationConnection, value)) CopyAdministrationConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    public RelayCommand AddConnectionCommand { get; }

    public RelayCommand SaveProfileCommand { get; }

    public RelayCommand CopyRuntimeConnectionCommand { get; }

    public RelayCommand CopyAdministrationConnectionCommand { get; }

    /// <summary>Shows the saved or first profile once the view is listening for <see cref="ProfileLoaded"/>.</summary>
    public void Start()
    {
        if (started) return;
        started = true;
        if (initialProfile is null) return;
        SetProperty(ref selectedProfile, initialProfile, nameof(SelectedProfile));
        Load(initialProfile);
    }

    private ConnectionAuthenticationMode AuthenticationMode =>
        UsesAzureCli ? ConnectionAuthenticationMode.AzureCli : ConnectionAuthenticationMode.ConnectionString;

    private void CaptureEditor()
    {
        if (editing is null) return;
        var connection = new ConnectionProfile(ProfileName.Trim(), secrets.Runtime, secrets.Administration, AuthenticationMode,
            FullyQualifiedNamespace.Trim());
        editing.ApplyDraft(connection, ColorHex ?? editing.ColorHex, WarningMessage.Trim());
    }

    private void Load(ProfileDraft draft)
    {
        editing = draft;
        ProfileName = draft.Name;
        AuthenticationModeIndex = draft.AuthenticationMode == ConnectionAuthenticationMode.AzureCli ? AzureCliIndex : 0;
        ProfileLoaded?.Invoke(draft);
        FullyQualifiedNamespace = draft.FullyQualifiedNamespace;
        ColorHex = draft.ColorHex;
        WarningMessage = draft.WarningMessage;
        Status = string.Empty;
    }

    private void AddConnection()
    {
        string name = "Connection 1";
        for (var number = 2; Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)); number++)
            name = $"Connection {number}";

        var draft = new ProfileDraft(Guid.NewGuid().ToString("N"), name, new ConnectionProfile(name, "", ""));
        Profiles.Add(draft);
        SelectedProfile = draft;
        ProfileAdded?.Invoke(draft);
        NameFocusRequested?.Invoke(true);
    }

    private void Copy(string secret, string label)
    {
        if (secret.Length == 0) return;
        try
        {
            copyText(secret);
            Status = $"{label} connection string copied.";
        }
        catch (ExternalException)
        {
            Status = ClipboardUnavailable;
        }
    }

    private async Task SaveProfileAsync()
    {
        if (editing is null || persistence.IsSaving) return;

        ProfileDraft editor = editing;
        var connection = new ConnectionProfile(ProfileName.Trim(), secrets.Runtime, secrets.Administration, AuthenticationMode,
            FullyQualifiedNamespace.Trim());
        var validation = ConnectionProfileValidator.Validate(connection);
        if (!validation.IsValid)
        {
            Status = string.Join(" ", validation.Errors);
            NameFocusRequested?.Invoke(false);
            return;
        }

        var savedProfile = new InvestigationProfile(editor.Id, connection, ColorHex ?? editor.ColorHex, WarningMessage.Trim());
        try
        {
            IsEditable = false;
            await persistence.SaveAsync(current => current with
            {
                Profiles = current.Profiles.Any(profile => profile.Id == savedProfile.Id)
                    ? current.Profiles.Select(profile => profile.Id == savedProfile.Id ? savedProfile : profile).ToArray()
                    : current.Profiles.Append(savedProfile).ToArray()
            });
            editor.Apply(savedProfile);
            if (ReferenceEquals(editing, editor)) Status = "Profile saved. No connection was attempted.";
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(editing, editor)) Status = $"Could not save profile: {exception.Message}";
        }
        finally
        {
            IsEditable = true;
        }
    }
}
