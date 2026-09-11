using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class TrayLifetimeProof
{
    public static async Task<int> RunAsync(PrototypeWindow window, string output)
    {
        Directory.CreateDirectory(output);
        var report = new List<string>();
        // Allow reporting after actual close/Exit. Program shuts down immediately after this proof.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            Check(TrayIsVisible(window), "A real Windows NotifyIcon is visible for the running application", report);
            window.SetWatched(window.Workspace.EntityPath, true, true);
            var before = window.Workspace.SnapshotMessages().Count;
            window.Close();
            await Settle();
            Check(!window.IsVisible && Application.Current.Windows.Cast<Window>().Contains(window) && TrayIsVisible(window),
                "Default Close hides the application while its real system tray icon remains active", report);
            await Task.Delay(TimeSpan.FromSeconds(15.5));
            await Settle();
            var notification = Application.Current.Windows.OfType<WatchNotificationWindow>().SingleOrDefault();
            Check(notification is { IsVisible: true } && window.Workspace.SnapshotMessages().Count > before,
                "The independent 15-second Watch timer creates a desktop notification while the main window is hidden", report);
            ProofCapture.Save(notification!, output, "tray-background-notification");
            var latest = window.Workspace.SnapshotMessages().Where(row => row.Source == window.Workspace.EntityPath && row.IsDeadLetter)
                .OrderByDescending(row => row.Enqueued).First();
            ProofCapture.Descendants(notification!).OfType<Button>().Single(button => Equals(button.Content, "Investigate"))
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle();
            for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
            await Settle();
            Check(window.IsVisible && window.WindowState != WindowState.Minimized && window.Workspace.FocusedMessage?.Key == latest.Key,
                "Investigate from the desktop notification restores the hidden application and selects the new message", report);
            window.SetWatched(window.Workspace.EntityPath, true, false);
            var settings = await OpenSettings(window);
            var closePreference = ProofCapture.Control<ToggleButton>(settings, "CloseToTrayToggle");
            Check(closePreference.IsChecked == true, "Settings exposes close-to-system-tray enabled by default", report);
            Toggle(closePreference);
            await Settle();
            Check(closePreference.IsChecked == false, "The Settings switch turns close-to-system-tray off", report);
            settings.Close();
            window.Close();
            await Settle();
            Check(!Application.Current.Windows.Cast<Window>().Contains(window) && !TrayIsVisible(window),
                "With close-to-tray disabled, Close closes the application window and disposes its tray icon", report);

            var exitWindow = new PrototypeWindow();
            exitWindow.ConfigureApplicationLifetime(false);
            exitWindow.Show();
            await Settle();
            Check(TrayIsVisible(exitWindow), "A second real application lifetime initializes its tray icon", report);
            settings = await OpenSettings(exitWindow);
            Check(ProofCapture.Control<ToggleButton>(settings, "CloseToTrayToggle").IsChecked == true,
                "A new prototype session restores the documented default close preference", report);
            settings.Close();
            InvokeTrayExit(exitWindow);
            await Settle();
            Check(!Application.Current.Windows.Cast<Window>().Contains(exitWindow) && !TrayIsVisible(exitWindow),
                "Explicit tray Exit closes the app and tray even when close-to-tray is enabled", report);
            Check(!Application.Current.Windows.OfType<WatchNotificationWindow>().Any(), "No watch notification windows remain after Exit", report);
            report.Add("- Scope: real NotifyIcon lifetime and desktop WPF notification verified; taskbar icon pixels and Windows toast delivery are not tested.");
            await File.WriteAllLinesAsync(Path.Combine(output, "tray-report.md"), report);
            return 0;
        }
        catch (Exception error)
        {
            report.Add("- FAIL: " + error);
            await File.WriteAllLinesAsync(Path.Combine(output, "tray-report.md"), report);
            return 1;
        }
    }

    private static bool TrayIsVisible(PrototypeWindow window)
    {
        var tray = typeof(PrototypeWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
        return tray?.GetType().GetField("icon", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(tray) is Forms.NotifyIcon { Visible: true };
    }

    private static async Task<PrototypeSettingsWindow> OpenSettings(PrototypeWindow window)
    {
        ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        return Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single(settings => settings.IsVisible);
    }

    private static void Toggle(ToggleButton button)
    {
        var peer = new ToggleButtonAutomationPeer(button);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
    }

    private static void InvokeTrayExit(PrototypeWindow window)
    {
        var tray = typeof(PrototypeWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var menu = (Forms.ContextMenuStrip)tray.GetType().GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
        menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "Exit").PerformClick();
    }
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
