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
            Check(window.IsVisible && window.WindowState != WindowState.Minimized && window.Workspace.FocusedMessage?.Key == latest.Key,
                "Investigate from the desktop notification restores the hidden application and selects the new message", report);
            window.SetWatched(window.Workspace.EntityPath, true, false);
            var menu = await OpenSettings(window);
            var closePreference = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Close to system tray"));
            Check(closePreference.IsChecked, "Settings exposes close-to-system-tray enabled by default", report);
            Invoke(closePreference);
            await Settle();
            Check(!closePreference.IsChecked, "The Settings menu action turns close-to-system-tray off", report);
            menu.IsOpen = false;
            window.Close();
            await Settle();
            Check(!Application.Current.Windows.Cast<Window>().Contains(window) && !TrayIsVisible(window),
                "With close-to-tray disabled, Close closes the application window and disposes its tray icon", report);

            var exitWindow = new PrototypeWindow();
            exitWindow.ConfigureApplicationLifetime(false);
            exitWindow.Show();
            await Settle();
            Check(TrayIsVisible(exitWindow), "A second real application lifetime initializes its tray icon", report);
            menu = await OpenSettings(exitWindow);
            Check(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Close to system tray")).IsChecked,
                "A new prototype session restores the documented default close preference", report);
            Invoke(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Exit")));
            await Settle();
            Check(!Application.Current.Windows.Cast<Window>().Contains(exitWindow) && !TrayIsVisible(exitWindow),
                "Explicit Settings Exit closes the app and tray even when close-to-tray is enabled", report);
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

    private static async Task<ContextMenu> OpenSettings(PrototypeWindow window)
    {
        ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Settle();
        return PresentationSource.CurrentSources.Cast<PresentationSource>().Where(source => source.RootVisual is not null)
            .SelectMany(source => ProofCapture.Descendants(source.RootVisual)).OfType<ContextMenu>().Single(menu => menu.IsOpen);
    }

    private static void Invoke(MenuItem item)
    {
        var peer = new MenuItemAutomationPeer(item);
        if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
            throw new InvalidOperationException($"Menu action has no Invoke provider: {item.Header}");
        invoke.Invoke();
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
