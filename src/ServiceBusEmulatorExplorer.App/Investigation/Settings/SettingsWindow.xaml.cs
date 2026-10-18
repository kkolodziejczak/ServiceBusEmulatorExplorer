using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

public sealed record DateFormatChoice(DateDisplayFormat Format, string Label);

public sealed record ProfileAccent(string Name, string ColorHex);

public partial class SettingsWindow : Window, IConnectionSecrets
{
    private readonly SettingsPersistence _persistence;
    private readonly Action<string> _copyText;

    public SettingsWindow(WorkspacePreferences preferences, Func<WorkspacePreferences, Task> saveAsync, bool trayAvailable = true,
        Action<string>? copyText = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(saveAsync);

        _persistence = new SettingsPersistence(preferences, saveAsync);
        General = new GeneralPreferencesViewModel(_persistence, trayAvailable);
        _copyText = copyText ?? Clipboard.SetText;
        Connections = new ConnectionProfilesViewModel(_persistence, this, _copyText);
        InitializeComponent();

        var activeProfile = preferences.Profiles.FirstOrDefault(profile => profile.Id == preferences.SelectedProfileId);
        ProfileTheme.Apply(this, activeProfile?.ColorHex ?? "#0069FA");

        ColorPicker.ItemsSource = new ProfileAccent[]
        {
            new("Blue", "#0069FA"), new("Purple", "#7540BF"), new("Teal", "#007F80"),
            new("Orange", "#B85B00"), new("Red", "#C83B3B")
        };
        DateFormatSelector.ItemsSource = Enum.GetValues<DateDisplayFormat>().Select(format => new DateFormatChoice(format,
            (format switch
            {
                DateDisplayFormat.Windows => "Follow Windows",
                DateDisplayFormat.Iso => "yyyy-MM-dd",
                DateDisplayFormat.DayFirst => "dd/MM/yyyy",
                DateDisplayFormat.MonthFirst => "MM/dd/yyyy",
                _ => "dd MMM yyyy"
            }) + " - " + DateDisplay.Date(new DateTime(2026, 10, 4), format))).ToArray();
        // Bind only once the code-built choices are in place, so each selection finds its item.
        DataContext = this;

        LicencesList.ItemsSource = ThirdPartyNotices.Components;
        LicencesList.SelectedIndex = 0;
        _persistence.PropertyChanged += (_, _) => DoneButton.IsEnabled = !_persistence.IsSaving;
        Connections.ProfileLoaded += ShowSecrets;
        Connections.ProfileAdded += draft => ProfilesList.ScrollIntoView(draft);
        Connections.NameFocusRequested += FocusProfileName;
        Connections.Start();
    }

    public WorkspacePreferences CurrentPreferences => _persistence.Saved;

    public GeneralPreferencesViewModel General { get; }

    public ConnectionProfilesViewModel Connections { get; }

    string IConnectionSecrets.Runtime => RuntimeConnection.Password;

    string IConnectionSecrets.Administration => AdministrationConnection.Password;

    private void ShowSecrets(ProfileDraft draft)
    {
        RuntimeConnection.Password = draft.RuntimeConnectionString;
        AdministrationConnection.Password = draft.AdministrationConnectionString;
    }

    private void FocusProfileName(bool selectAll)
    {
        ProfileName.Focus();
        if (selectAll) ProfileName.SelectAll();
    }

    private void ConnectionString_Changed(object sender, RoutedEventArgs e)
    {
        // Only whether a field is empty crosses to the view model; the text is read when it is needed.
        Connections.HasRuntimeConnection = HasConnectionString(RuntimeConnection);
        Connections.HasAdministrationConnection = HasConnectionString(AdministrationConnection);
    }

    private static bool HasConnectionString(PasswordBox field)
    {
        using var password = field.SecurePassword;
        return password.Length > 0;
    }

    private void Licence_Selected(object sender, SelectionChangedEventArgs e)
    {
        LicenceStatus.Text = string.Empty;
        LicenceText.Text = (LicencesList.SelectedItem as ThirdPartyComponent)?.Notice ?? string.Empty;
        LicenceText.ScrollToHome();
    }

    private void CopyLicence_Click(object sender, RoutedEventArgs e)
    {
        if (LicencesList.SelectedItem is not ThirdPartyComponent component) return;
        try
        {
            _copyText(LicenceText.Text);
            LicenceStatus.Text = $"{component.Name} licence copied.";
        }
        catch (ExternalException)
        {
            LicenceStatus.Text = "Clipboard is unavailable. Try copying again.";
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_persistence.IsSaving) return;

        e.Cancel = true;
        General.ShowCloseBlocked();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
