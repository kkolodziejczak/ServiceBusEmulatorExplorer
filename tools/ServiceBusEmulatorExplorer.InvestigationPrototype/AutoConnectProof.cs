using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class AutoConnectProof
{
    public static async Task Exercise(PrototypeWindow owner, List<string> report, string output)
    {
        var path = Path.Combine(output, "auto-connect-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new PrototypeWindow();
        window.ConfigureApplicationLifetime(true);
        Call(window, "InitializePreferences", path);
        try
        {
            window.Show();
            await Settle();
            var selector = (ComboBox)window.FindName("ConnectionSelector");
            var original = (PrototypeConnectionProfile)selector.SelectedItem;
            var other = selector.Items.Cast<PrototypeConnectionProfile>().First(profile => profile != original);
            var settings = await Settings(window);
            var toggle = (ToggleButton)settings.FindName("AutoConnectToggle");
            Check(toggle.IsChecked == false, "Automatically connect on profile switch defaults to off", report);
            Click(settings, "DoneButton");
            selector.SelectedItem = other;
            await Settle();
            Check(!window.Workspace.IsConnected, "Switching profiles with auto-connect off leaves the connection disconnected", report);
            settings = await Settings(window);
            toggle = (ToggleButton)settings.FindName("AutoConnectToggle");
            ((IToggleProvider)(UIElementAutomationPeer.CreatePeerForElement(toggle) ?? new ToggleButtonAutomationPeer(toggle)).GetPattern(PatternInterface.Toggle)).Toggle();
            Click(settings, "DoneButton");
            selector.SelectedItem = original;
            await Settle();
            Check(window.Workspace.IsConnected, "Enabling auto-connect connects the newly selected profile", report);
            other.WarningMessage = "Approve before connecting this proof profile.";
            var prompts = Respond(() => selector.SelectedItem = other, false);
            await Settle();
            Check(prompts == 1 && ReferenceEquals(selector.SelectedItem, original) && window.Workspace.IsConnected,
                "Auto-connect warning cancellation keeps the previously connected profile", report);
            prompts = Respond(() => selector.SelectedItem = other, true);
            await Settle();
            Check(prompts == 1 && ReferenceEquals(selector.SelectedItem, other) && window.Workspace.IsConnected,
                "Approving auto-connect warning connects once without a second prompt", report);
            Click(window, "ConnectionButton");
            settings = await Settings(window);
            ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
            await Settle();
            ((TextBox)settings.FindName("ProfileName")).Text = other.Name + " edited";
            Click(settings, "SaveProfileButton");
            await Settle();
            Check(!window.Workspace.IsConnected, "Saving the current profile does not trigger auto-connect", report);
            Click(settings, "DoneButton");
            window.Close();
            await Settle();
            var store = new PrototypePreferencesStore(path);
            Check(store.TryLoad(out var preferences, out _) && preferences.AutoConnectOnSwitch,
                "Auto-connect preference persists to an isolated preferences file", report);
            var reopened = new PrototypeWindow();
            reopened.ConfigureApplicationLifetime(true);
            Call(reopened, "InitializePreferences", path);
            try
            {
                reopened.Show();
                await Settle();
                settings = await Settings(reopened);
                Check(((ToggleButton)settings.FindName("AutoConnectToggle")).IsChecked == true,
                    "A fresh Settings window restores the saved auto-connect switch", report);
                ProofCapture.Save(settings, output, "auto-connect-settings");
                Click(settings, "DoneButton");
            }
            finally { reopened.Close(); }
        }
        finally { if (window.IsLoaded) window.Close(); }
        await Settle();
        owner.Activate();
    }

    private static int Respond(Action action, bool accept)
    {
        var count = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            foreach (var warning in Application.Current.Windows.OfType<ProfileWarningWindow>().ToArray())
            {
                count++;
                Click(warning, accept ? "ContinueButton" : "CancelButton");
            }
        };
        timer.Start();
        try { action(); }
        finally { timer.Stop(); }
        return count;
    }

    private static async Task<PrototypeSettingsWindow> Settings(PrototypeWindow window)
    {
        ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        return Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single();
    }
    private static void Call(PrototypeWindow window, string method, params object[] args) => typeof(PrototypeWindow)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
