using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Settings;

/// <summary>
/// The Settings window: its three tabs share one <see cref="SettingsPersistence"/>, and the window may close only once
/// nothing is saving (DEC-053).
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

    /// <summary>Whether the window may close now; while something is saving it may not, and the General tab says why.</summary>
    public bool CanClose()
    {
        if (!persistence.IsSaving) return true;
        General.ShowCloseBlocked();
        return false;
    }
}
