using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>The answer to the Save changes? question asked when closing Settings with unsaved profile edits.</summary>
public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

/// <summary>
/// The Settings window: its three tabs share one <see cref="SettingsPersistence"/>, and the window may close only once
/// nothing is saving and unsaved profile edits are saved or discarded (DEC-053, Settings 70).
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsPersistence persistence;

    public SettingsViewModel(WorkspacePreferences preferences, Func<WorkspacePreferences, Task> saveAsync, bool trayAvailable,
        IConnectionSecrets secrets, Action<string> copyText, IReadOnlyList<ILicensedComponent> licences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(saveAsync);
        persistence = new SettingsPersistence(preferences, saveAsync);
        General = new GeneralPreferencesViewModel(persistence, trayAvailable);
        Connections = new ConnectionProfilesViewModel(persistence, secrets, copyText);
        Licences = new SettingsLicencesViewModel(licences, copyText);
        DoneCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => !persistence.IsSaving);
        persistence.PropertyChanged += (_, _) => DoneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Raised when the user is done, so the view can close.</summary>
    public event Action? CloseRequested;

    /// <summary>The preferences as last saved successfully.</summary>
    public WorkspacePreferences SavedPreferences => persistence.Saved;

    public GeneralPreferencesViewModel General { get; }

    public ConnectionProfilesViewModel Connections { get; }

    public SettingsLicencesViewModel Licences { get; }

    public RelayCommand DoneCommand { get; }

    /// <summary>Asks what to do with unsaved profile edits; the view shows the question. Until set, nothing closes.</summary>
    public Func<string, UnsavedChangesChoice> ConfirmUnsavedChanges { get; set; } = _ => UnsavedChangesChoice.Cancel;

    /// <summary>
    /// Whether the window may close now. While something is saving it may not, and the General tab says why. With
    /// unsaved profile edits it asks first; choosing Save keeps it open and asks to close once every edit is saved.
    /// </summary>
    public bool CanClose()
    {
        if (persistence.IsSaving)
        {
            General.ShowCloseBlocked();
            return false;
        }

        if (!Connections.HasUnsavedChanges) return true;
        switch (ConfirmUnsavedChanges(Connections.UnsavedChangesQuestion()))
        {
            case UnsavedChangesChoice.Discard:
                return true;
            case UnsavedChangesChoice.Save:
                _ = SaveAndCloseAsync();
                return false;
            default:
                return false;
        }
    }

    private async Task SaveAndCloseAsync()
    {
        if (!await Connections.SaveAllAsync()) return;
        // A General change made while the profiles saved is queued behind them; close once it is saved too.
        await persistence.WhenIdleAsync();
        CloseRequested?.Invoke();
    }
}
