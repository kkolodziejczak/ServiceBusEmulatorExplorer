using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

public sealed record DateFormatChoice(DateDisplayFormat Format, string Label);

public sealed record ProfileAccent(string Name, string ColorHex);

/// <summary>
/// The WPF view of <see cref="SettingsViewModel"/>. It keeps what only WPF can do: the profile theme, the password
/// boxes, focus and scrolling, the code-built choice lists and actually closing (DEC-053).
/// </summary>
public partial class SettingsWindow : Window, IConnectionSecrets
{
    private readonly SettingsViewModel viewModel;

    public SettingsWindow(WorkspacePreferences preferences, Func<WorkspacePreferences, Task> saveAsync, bool trayAvailable = true,
        Action<string>? copyText = null)
    {
        viewModel = new SettingsViewModel(preferences, saveAsync, trayAvailable, this, copyText ?? Clipboard.SetText,
            ThirdPartyNotices.Components);
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
        DataContext = viewModel;

        viewModel.CloseRequested += Close;
        viewModel.Licences.NoticeShown += () => LicenceText.ScrollToHome();
        viewModel.Connections.ProfileLoaded += ShowSecrets;
        viewModel.Connections.ProfileAdded += draft => ProfilesList.ScrollIntoView(draft);
        viewModel.Connections.NameFocusRequested += FocusProfileName;
        viewModel.Connections.Start();
    }

    public WorkspacePreferences CurrentPreferences => viewModel.SavedPreferences;

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
        viewModel.Connections.HasRuntimeConnection = HasConnectionString(RuntimeConnection);
        viewModel.Connections.HasAdministrationConnection = HasConnectionString(AdministrationConnection);
    }

    private static bool HasConnectionString(PasswordBox field)
    {
        using var password = field.SecurePassword;
        return password.Length > 0;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!viewModel.CanClose()) e.Cancel = true;
    }
}
