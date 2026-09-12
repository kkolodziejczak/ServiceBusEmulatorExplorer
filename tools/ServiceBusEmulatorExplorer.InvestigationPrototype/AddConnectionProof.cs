using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class AddConnectionProof
{
    public static async Task Exercise(PrototypeWindow owner, List<string> report, string output)
    {
        var window = new PrototypeWindow();
        window.ConfigureApplicationLifetime(true);
        try
        {
            window.Show();
            await Settle();
            var connections = (PrototypeConnectionSettings)typeof(PrototypeWindow).GetField("connectionSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var active = connections.SelectedProfile;
            var originalIds = connections.Profiles.Select(profile => profile.Id).ToArray();
            ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle();
            var settings = Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single();
            ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
            await Settle();
            Click(settings, "AddConnectionButton");
            await Settle();
            var added = (PrototypeConnectionProfile)((ListBox)settings.FindName("ProfilesList")).SelectedItem;
            var id = added.Id;
            Check(connections.Profiles.Count == 3 && !originalIds.Contains(id) && !string.IsNullOrWhiteSpace(id)
                && added.RuntimeConnection.Length == 0 && added.AdministrationConnection.Length == 0,
                "Add connection creates a third profile with a unique identity and blank connection strings", report);
            Check(ReferenceEquals(connections.SelectedProfile, active), "Adding a connection preserves the active profile", report);
            ((TextBox)settings.FindName("ProfileName")).Text = "Added proof profile";
            ((TextBox)settings.FindName("ConnectionWarning")).Text = "Proof connection warning";
            var palette = (ListBox)settings.FindName("ColorPicker");
            palette.SelectedIndex = 2;
            ((PasswordBox)settings.FindName("RuntimeConnection")).Password = "added-runtime-proof-secret";
            ((PasswordBox)settings.FindName("AdministrationConnection")).Password = "added-admin-proof-secret";
            Click(settings, "SaveProfileButton");
            await Settle();
            Check(added.Id == id && added.Name == "Added proof profile" && added.WarningMessage == "Proof connection warning"
                && added.ColorHex == ((ProfileAccent)palette.SelectedItem).ColorHex && added.RuntimeConnection == "added-runtime-proof-secret",
                "Saving a new connection retains identity and edited name, warning, color and credentials", report);
            Check(((ComboBox)window.FindName("ConnectionSelector")).Items.Count == 3 && ReferenceEquals(connections.SelectedProfile, active),
                "Saved new profile appears in the main selector without switching the active connection", report);
            ProofCapture.Save(settings, output, "add-connection");
            Click(settings, "DoneButton");
            await Settle();
            var path = Path.Combine(output, "added-profile-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new PrototypePreferencesStore(path);
            var saved = new PrototypePreferences { Profiles = connections.Profiles.ToList(), SelectedProfileId = active.Id };
            Check(store.TrySave(saved, out _) && store.TryLoad(out var restored, out _) && restored.Profiles.Count == 3
                && restored.Profiles.Any(profile => profile.Id == id && profile.WarningMessage == added.WarningMessage
                    && profile.RuntimeConnection == added.RuntimeConnection && profile.ColorHex == added.ColorHex),
                "Added profile survives a protected preferences round trip through an isolated proof file", report);
            Check(!File.ReadAllText(path).Contains("added-runtime-proof-secret"), "New profile credentials are absent as plaintext from persisted preferences", report);
            await WatchSearch(window, report, output);
        }
        finally { window.Close(); }
        await Settle();
        owner.Activate();
    }

    private static async Task WatchSearch(PrototypeWindow owner, List<string> report, string output)
    {
        var dialog = new GlobalWatchWindow(owner.Workspace.Roots, new WatchRules(), () => { }) { Owner = owner };
        try
        {
            dialog.Show();
            await Settle();
            var input = (TextBox)dialog.FindName("InclusionSearch");
            var placeholder = (TextBlock)dialog.FindName("InclusionSearchPlaceholder");
            Check(placeholder.IsVisible && placeholder.Text == "Search entities", "Watch search displays its placeholder when empty", report);
            Check(!ProofCapture.Descendants(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("Choose message types")
                || text.Text.Contains("Check Queues or Topics") || text.Text.Contains("Changes apply immediately")),
                "Watch dialog omits redundant introductory and instructional paragraphs", report);
            ProofCapture.Save(dialog, output, "watch-search-empty");
            input.Text = "billing";
            await Settle();
            Check(!placeholder.IsVisible && ((TreeView)dialog.FindName("InclusionTree")).Items.Count > 0,
                "Typing filters Watch entities and hides its search placeholder", report);
            ProofCapture.Save(dialog, output, "watch-search-typed");
            input.Clear();
            await Settle();
            Check(placeholder.IsVisible, "Clearing Watch search restores its placeholder", report);
        }
        finally { dialog.Close(); }
    }

    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
