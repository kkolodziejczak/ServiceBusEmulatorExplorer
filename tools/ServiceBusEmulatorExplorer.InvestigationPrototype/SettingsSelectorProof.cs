using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class SettingsSelectorProof
{
    public static async Task Exercise(PrototypeWindow window, List<string> report, string output)
    {
        Click(window, "WatchButton");
        await Settle();
        var popup = (Popup)window.FindName("WatchPopup");
        var active = (CheckBox)window.FindName("WatchActiveChoice");
        var dlq = (CheckBox)window.FindName("WatchDlqChoice");
        Check(popup.IsOpen && active.IsVisible && dlq.IsVisible, "Watch opens a selector with independent Active and DLQ choices", report);
        Check(active.IsChecked == false && dlq.IsChecked == false, "Unwatched entity starts with both Watch choices off", report);
        Toggle(active);
        await Settle();
        Check(active.IsChecked == true && dlq.IsChecked == false && popup.IsOpen, "Active can be enabled independently without closing the Watch selector", report);
        Toggle(dlq);
        await Settle();
        Check(active.IsChecked == true && dlq.IsChecked == true && popup.IsOpen, "Both Watch buckets can be enabled in one selector visit", report);
        SavePopup(popup, output, "watch-selector");
        popup.IsOpen = false;
        Click(window, "WatchButton");
        await Settle();
        Check(active.IsChecked == true && dlq.IsChecked == true, "Reopened Watch selector retains both selected buckets", report);
        Click(window, "StopWatchingButton");
        await Settle();
        Check(!popup.IsOpen && ((TextBlock)window.FindName("WatchButtonLabel")).Text == "Watch", "Stop watching clears the entity Watch state and closes its selector", report);
        Click(window, "WatchButton");
        await Settle();
        Check(active.IsChecked == false && dlq.IsChecked == false, "Stop watching resets both choices on reopening", report);
        popup.IsOpen = false;
        await ExerciseSettings(window, report, output);
        await ExerciseRefresh(window, report, output);
    }

    private static async Task ExerciseRefresh(PrototypeWindow window, List<string> report, string output)
    {
        var interval = (ComboBox)window.FindName("AutoInterval");
        var original = interval.SelectedIndex;
        interval.IsDropDownOpen = true;
        await Settle();
        var popup = (Popup)interval.Template.FindName("PART_Popup", interval);
        Check(popup is { IsOpen: true } && interval.Items.Count == 4, "Refresh selector opens all four supported intervals", report);
        SavePopup(popup, output, "refresh-selector");
        interval.SelectedIndex = 3;
        interval.IsDropDownOpen = false;
        await Settle();
        Check(Equals(((ComboBoxItem)interval.SelectedItem).Content, "Auto: 30s"), "Styled refresh selector retains selectable 30-second interval", report);
        interval.SelectedIndex = original;
        await Settle();
    }
    private static async Task ExerciseSettings(PrototypeWindow window, List<string> report, string output)
    {
        var settings = await OpenSettings(window);
        Check(settings.Owner == window && settings.IsVisible, "Settings opens a dedicated owned window", report);
        var close = ProofCapture.Control<ToggleButton>(settings, "CloseToTrayToggle");
        var notifications = ProofCapture.Control<ToggleButton>(settings, "NotificationsToggle");
        Check(close.IsChecked == true && notifications.IsChecked == true, "General settings expose enabled close-to-tray and notification defaults", report);
        ProofCapture.CheckBounds(settings, [close, notifications]);
        ProofCapture.Save(settings, output, "settings-general");
        Toggle(close);
        Toggle(notifications);
        await Settle();
        Check(close.IsChecked == false && notifications.IsChecked == false, "General switches respond independently to UI Automation Toggle", report);
        Click(settings, "DoneButton");
        await Settle();
        Check(!settings.IsVisible, "Done closes Settings", report);
        settings = await OpenSettings(window);
        close = ProofCapture.Control<ToggleButton>(settings, "CloseToTrayToggle");
        notifications = ProofCapture.Control<ToggleButton>(settings, "NotificationsToggle");
        Check(close.IsChecked == false && notifications.IsChecked == false, "Reopening Settings retains this session's general preferences", report);
        Toggle(close);
        Toggle(notifications);
        ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
        await Settle();
        var runtime = ProofCapture.Control<PasswordBox>(settings, "RuntimeConnection");
        var admin = ProofCapture.Control<PasswordBox>(settings, "AdministrationConnection");
        var name = ProofCapture.Control<TextBox>(settings, "ProfileName");
        Check(runtime.IsVisible && admin.IsVisible && name.IsVisible, "Connections page exposes named profile and masked runtime and administration fields", report);
        var profileName = name.Text;
        var originalRuntime = runtime.Password;
        var originalAdmin = admin.Password;
        runtime.Password = "Endpoint=sb://localhost;SharedAccessKey=proof-runtime;UseDevelopmentEmulator=true;";
        admin.Password = "Endpoint=sb://localhost:5300;SharedAccessKey=proof-admin;UseDevelopmentEmulator=true;";
        Click(settings, "SaveProfileButton");
        await Settle();
        ProofCapture.CheckBounds(settings, [runtime, admin, name, ProofCapture.Control<Button>(settings, "SaveProfileButton")]);
        ProofCapture.Save(settings, output, "settings-connections");
        Click(settings, "DoneButton");
        await Settle();
        settings = await OpenSettings(window);
        ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
        await Settle();
        runtime = ProofCapture.Control<PasswordBox>(settings, "RuntimeConnection");
        admin = ProofCapture.Control<PasswordBox>(settings, "AdministrationConnection");
        Check(runtime.Password.Contains("proof-runtime") && admin.Password.Contains("proof-admin"), "Saved connection draft survives closing and reopening Settings within this session", report);
        Check(ProofCapture.Control<TextBox>(settings, "ProfileName").Text == profileName, "Saving connection draft retains its profile identity", report);
        settings.Width = 460;
        settings.Height = 520;
        ProofCapture.Control<Button>(settings, "SaveProfileButton").BringIntoView();
        await Settle();
        ProofCapture.CheckBounds(settings, [ProofCapture.Control<Button>(settings, "SaveProfileButton"), ProofCapture.Control<Button>(settings, "DoneButton")]);
        Check(true, "Compact Settings scrolls the form to keep Save and Done reachable", report);
        ProofCapture.Save(settings, output, "settings-compact");
        runtime.Password = originalRuntime;
        admin.Password = originalAdmin;
        Click(settings, "SaveProfileButton");
        ((TabItem)settings.FindName("GeneralTab")).IsSelected = true;
        await Settle();
        Check(ProofCapture.Control<ToggleButton>(settings, "CloseToTrayToggle").IsVisible, "General tab returns from Connections to preferences", report);
        Click(settings, "DoneButton");
        await Settle();
    }

    private static async Task<PrototypeSettingsWindow> OpenSettings(PrototypeWindow window)
    {
        ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        return Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single(settings => settings.IsVisible);
    }

    private static void SavePopup(Popup popup, string output, string name)
    {
        var root = (FrameworkElement)popup.Child;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }

    private static void Toggle(ToggleButton button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button) ?? new ToggleButtonAutomationPeer(button);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
    }

    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
