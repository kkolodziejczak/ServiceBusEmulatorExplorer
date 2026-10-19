using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// The Connections tab of Settings: one editor for the selected profile, a draft kept per profile while switching,
/// a Modified mark on each profile with unsaved edits, validation before saving, and exact copies of the typed
/// connection strings (DEC-053, Settings 70).
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
    private bool hasUnsavedChanges;
    private bool started;
    private bool loading;

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

    /// <summary>Raised when saving every profile at once left one unsaved, so the view can show this tab.</summary>
    public event Action? AttentionRequested;

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
        set
        {
            if (SetProperty(ref profileName, value ?? string.Empty)) EditorChanged();
        }
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
            EditorChanged();
        }
    }

    public bool UsesConnectionStrings => !UsesAzureCli;

    public bool UsesAzureCli => AuthenticationModeIndex == AzureCliIndex;

    public string FullyQualifiedNamespace
    {
        get => fullyQualifiedNamespace;
        set
        {
            if (SetProperty(ref fullyQualifiedNamespace, value ?? string.Empty)) EditorChanged();
        }
    }

    /// <summary>The chosen accent; null when the profile's colour is not one of the offered ones.</summary>
    public string? ColorHex
    {
        get => colorHex;
        set
        {
            if (SetProperty(ref colorHex, value)) EditorChanged();
        }
    }

    public string WarningMessage
    {
        get => warningMessage;
        set
        {
            if (SetProperty(ref warningMessage, value ?? string.Empty)) EditorChanged();
        }
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

    /// <summary>Whether any profile is <see cref="ProfileDraft.IsModified"/>; the tab shows Modified while it is.</summary>
    public bool HasUnsavedChanges
    {
        get => hasUnsavedChanges;
        private set => SetProperty(ref hasUnsavedChanges, value);
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

    /// <summary>
    /// Called by the view whenever a connection string field changes. The text is read only to compare it with the
    /// saved profile.
    /// </summary>
    public void ConnectionStringsChanged() => EditorChanged();

    /// <summary>The Save changes? question for closing, naming every profile with unsaved edits.</summary>
    public string UnsavedChangesQuestion()
    {
        string[] names = Profiles.Where(profile => profile.IsModified)
            .Select(profile => ReferenceEquals(profile, editing) ? ProfileName.Trim() : profile.Name)
            .Select(name => name.Length == 0 ? "an unnamed profile" : name)
            .ToArray();
        string listed = names.Length < 2 ? string.Concat(names) : $"{string.Join(", ", names[..^1])} and {names[^1]}";
        return $"You have unsaved changes to {listed}. What do you want to do?";
    }

    /// <summary>
    /// Saves every profile with unsaved edits in one write, as chosen when closing. Returns false while any stays
    /// unsaved: the first invalid one is shown with its errors, or a failed write is reported.
    /// </summary>
    public async Task<bool> SaveAllAsync()
    {
        if (persistence.IsSaving) return false;
        CaptureEditor();
        var checkedProfiles = Profiles.Where(draft => draft.IsModified)
            .Select(draft => (Draft: draft, Profile: draft.ToProfile()))
            .Select(edit => (edit.Draft, edit.Profile, Validation: ConnectionProfileValidator.Validate(edit.Profile.Connection)))
            .ToArray();
        var valid = checkedProfiles.Where(check => check.Validation.IsValid).Select(check => (check.Draft, check.Profile)).ToArray();
        try
        {
            if (valid.Length > 0) await SaveAsync(valid);
        }
        catch (Exception exception)
        {
            Status = $"Could not save profiles: {exception.Message}";
            AttentionRequested?.Invoke();
            return false;
        }

        var (invalid, _, validation) = checkedProfiles.FirstOrDefault(check => !check.Validation.IsValid);
        if (invalid is null) return true;
        AttentionRequested?.Invoke();
        SelectedProfile = invalid;
        Status = string.Join(" ", validation.Errors);
        NameFocusRequested?.Invoke(false);
        return false;
    }

    private ConnectionAuthenticationMode AuthenticationMode =>
        UsesAzureCli ? ConnectionAuthenticationMode.AzureCli : ConnectionAuthenticationMode.ConnectionString;

    private void EditorChanged()
    {
        if (editing is not null && !loading) UpdateModified(editing);
    }

    private void UpdateModified(ProfileDraft draft)
    {
        InvestigationProfile current = ReferenceEquals(draft, editing) ? EditorProfile(draft) : draft.ToProfile();
        draft.IsModified = current != persistence.Saved.Profiles.FirstOrDefault(profile => profile.Id == draft.Id);
        HasUnsavedChanges = Profiles.Any(profile => profile.IsModified);
    }

    /// <summary>The editor's fields as they would be saved for <paramref name="draft"/>.</summary>
    private InvestigationProfile EditorProfile(ProfileDraft draft) =>
        new(draft.Id, new ConnectionProfile(ProfileName.Trim(), secrets.Runtime, secrets.Administration, AuthenticationMode,
            FullyQualifiedNamespace.Trim()), ColorHex ?? draft.ColorHex, WarningMessage.Trim());

    private void CaptureEditor()
    {
        if (editing is not null) editing.Apply(EditorProfile(editing));
    }

    private void Load(ProfileDraft draft)
    {
        editing = draft;
        loading = true;
        try
        {
            ProfileName = draft.Name;
            AuthenticationModeIndex = draft.AuthenticationMode == ConnectionAuthenticationMode.AzureCli ? AzureCliIndex : 0;
            ProfileLoaded?.Invoke(draft);
            FullyQualifiedNamespace = draft.FullyQualifiedNamespace;
            ColorHex = draft.ColorHex;
            WarningMessage = draft.WarningMessage;
            Status = string.Empty;
        }
        finally
        {
            loading = false;
        }

        UpdateModified(draft);
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
        InvestigationProfile profile = EditorProfile(editor);
        var validation = ConnectionProfileValidator.Validate(profile.Connection);
        if (!validation.IsValid)
        {
            Status = string.Join(" ", validation.Errors);
            NameFocusRequested?.Invoke(false);
            return;
        }

        try
        {
            await SaveAsync([(editor, profile)]);
            if (ReferenceEquals(editing, editor)) Status = "Profile saved. No connection was attempted.";
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(editing, editor)) Status = $"Could not save profile: {exception.Message}";
        }
    }

    /// <summary>Writes the given profiles in one save, replacing saved ones by ID and appending new ones, then clears their marks.</summary>
    private async Task SaveAsync(IReadOnlyList<(ProfileDraft Draft, InvestigationProfile Profile)> changes)
    {
        try
        {
            IsEditable = false;
            await persistence.SaveAsync(current =>
            {
                var replaced = current.Profiles.Select(saved =>
                    changes.Select(change => change.Profile).FirstOrDefault(profile => profile.Id == saved.Id) ?? saved);
                var added = changes.Select(change => change.Profile).Where(profile => current.Profiles.All(saved => saved.Id != profile.Id));
                return current with { Profiles = replaced.Concat(added).ToArray() };
            });
            foreach (var (draft, profile) in changes)
            {
                draft.Apply(profile);
                UpdateModified(draft);
            }
        }
        finally
        {
            IsEditable = true;
        }
    }
}
