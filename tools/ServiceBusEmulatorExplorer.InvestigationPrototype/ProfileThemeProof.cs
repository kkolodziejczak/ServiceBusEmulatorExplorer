using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class ProfileThemeProof
{
    public static async Task Exercise(PrototypeWindow owner, List<string> report, string output)
    {
        var window = new PrototypeWindow();
        window.ConfigureApplicationLifetime(true);
        try
        {
            window.Show();
            await Settle();
            await Palette(window, report, output);
            await ConnectionWarnings(window, report, output);
        }
        finally { window.Close(); }
        await PendingWarningSave(report, output);
        await StartupWarning(report, output);
        owner.Activate();
    }

    private static async Task Palette(PrototypeWindow window, List<string> report, string output)
    {
        var selector = (ComboBox)window.FindName("ConnectionSelector");
        var profile = (PrototypeConnectionProfile)selector.SelectedItem;
        var original = profile.ColorHex;
        foreach (var color in new[] { "#0069FA", "#7540BF", "#007F80", "#B85B00", "#C83B3B" })
        {
            profile.ColorHex = color;
            Call(window, "RefreshConnectionPresentation");
            await Settle();
            var primary = Brush(window.FindResource("PrimaryBrush"));
            var selection = Brush(window.FindResource("SelectionBrush"));
            Check(primary == (Color)ColorConverter.ConvertFromString(color), "Profile primary resource follows " + color, report);
            Check(Brush(((Button)window.FindName("WatchButton")).Background) == primary,
                "Rendered primary Watch button follows " + color, report);
            Check(Brush(((ToggleButton)window.FindName("ActiveTab")).Foreground) == primary,
                "Selected Active tab follows " + color, report);
            var grid = (DataGrid)window.FindName("MessageGrid");
            grid.ScrollIntoView(window.Workspace.FocusedMessage);
            await Settle();
            var cells = ProofCapture.Descendants(grid).OfType<DataGridCell>().Where(cell => cell.IsSelected).ToArray();
            Check(cells.Length > 0 && cells.All(cell => Brush(cell.Background) == selection),
                "Selected message cells use the profile selection tint for " + color, report);
            Check(Brush(((Border)window.FindName("ToolbarSeparator")).Background) == Brush(window.FindResource("ChromeBrush")),
                "Header chrome uses profile theme for " + color, report);
            if (color is "#7540BF" or "#C83B3B")
            {
                var name = color == "#7540BF" ? "purple" : "red";
                ProofCapture.Save(window, output, "profile-theme-" + name);
                ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await Settle();
                var settings = Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single();
                Check(Brush(settings.FindResource("PrimaryBrush")) == primary, "Settings inherits the " + name + " profile theme", report);
                ProofCapture.Save(settings, output, "profile-theme-settings-" + name);
                settings.Close();
                Click(window, "GlobalWatchButton");
                await Settle();
                var watch = Application.Current.Windows.OfType<GlobalWatchWindow>().Single();
                Check(Brush(watch.FindResource("PrimaryBrush")) == primary, "Global Watch inherits the " + name + " profile theme", report);
                ProofCapture.Save(watch, output, "profile-theme-watch-" + name);
                watch.Close();
            }
        }
        profile.ColorHex = original;
        Call(window, "RefreshConnectionPresentation");
    }

    private static async Task ConnectionWarnings(PrototypeWindow window, List<string> report, string output)
    {
        var selector = (ComboBox)window.FindName("ConnectionSelector");
        var original = (PrototypeConnectionProfile)selector.SelectedItem;
        var target = selector.Items.Cast<PrototypeConnectionProfile>().First(profile => !ReferenceEquals(profile, original));
        target.WarningMessage = "Production-like sample profile. Investigate carefully.\nThis warning requires explicit confirmation.";
        var query = MessageSearchQuery.QuoteLiteral(window.Workspace.Messages[0].CorrelationId);
        ((TextBox)window.FindName("SearchBox")).Text = query;
        Call(window, "BeginGlobalSearch", false);
        await Complete(window);
        var keys = window.Workspace.Messages.Select(row => row.Key).ToArray();
        await Respond(() => selector.SelectedItem = target, true, false, output, "profile-warning-cancel", report);
        Check(ReferenceEquals(selector.SelectedItem, original) && window.Workspace.IsConnected
            && window.Workspace.IsCorrelationSearch && window.Workspace.CorrelationQuery == query
            && window.Workspace.Messages.Select(row => row.Key).SequenceEqual(keys),
            "Cancelling a profile warning preserves the current connection, search and results", report);
        await Respond(() => selector.SelectedItem = target, true, true, output, "profile-warning-continue", report);
        Check(ReferenceEquals(selector.SelectedItem, target) && !window.Workspace.IsConnected,
            "Accepting a profile warning selects that profile and leaves it disconnected", report);
        await Respond(() => Click(window, "ConnectionButton"), false, false, output, "profile-warning-acknowledged", report);
        Check(window.Workspace.IsConnected, "First Connect consumes the profile-switch acknowledgment without a duplicate prompt", report);
        Click(window, "ConnectionButton");
        await Respond(() => Click(window, "ConnectionButton"), true, false, output, "profile-warning-reconnect-cancel", report);
        Check(!window.Workspace.IsConnected, "Cancelling a later reconnect warning keeps the profile disconnected", report);
        await Respond(() => Click(window, "ConnectionButton"), true, true, output, "profile-warning-reconnect", report);
        Check(window.Workspace.IsConnected, "Accepting a later reconnect warning reconnects sample data", report);
    }

    private static async Task PendingWarningSave(List<string> report, string output)
    {
        var path = Path.Combine(output, "pending-warning-preferences-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new PrototypePreferencesStore(path);
        var window = new PrototypeWindow();
        window.ConfigureApplicationLifetime(true);
        try
        {
            Call(window, "InitializePreferences", path);
            window.Show();
            await Settle();
            var selector = (ComboBox)window.FindName("ConnectionSelector");
            var approved = (PrototypeConnectionProfile)selector.SelectedItem;
            var target = selector.Items.Cast<PrototypeConnectionProfile>().First(profile => !ReferenceEquals(profile, approved));
            target.WarningMessage = "Approval is pending. Do not persist this selection yet.";
            var query = MessageSearchQuery.QuoteLiteral(window.Workspace.Messages[0].CorrelationId);
            ((TextBox)window.FindName("SearchBox")).Text = query;
            Call(window, "BeginGlobalSearch", false);
            await Complete(window);
            void SaveWhilePending()
            {
                // Exercise the same save entry point used by the timer while the modal pumps messages.
                Call(window, "SavePreferences");
                Check(store.TryLoad(out var saved, out _) && saved.SelectedProfileId == approved.Id
                    && saved.Connected && saved.AppliedSearch == query,
                    "Saving during a pending warning retains the approved profile, connected state and search", report);
            }
            await Respond(() => selector.SelectedItem = target, true, false, output, "profile-warning-pending-header-save", report, SaveWhilePending);
            Check(ReferenceEquals(selector.SelectedItem, approved), "Header warning cancellation retains the approved profile after an in-modal save", report);
            ProofCapture.Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Settings")
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Settle();
            var settings = Application.Current.Windows.OfType<PrototypeSettingsWindow>().Single();
            ((TabItem)settings.FindName("ConnectionsTab")).IsSelected = true;
            ((ListBox)settings.FindName("ProfilesList")).SelectedItem = target;
            Click(settings, "SaveProfileButton");
            Check(ReferenceEquals(selector.SelectedItem, approved) && window.Workspace.CorrelationQuery == query,
                "Managing an inactive profile preserves the approved connection and applied search", report);
            settings.Close();
        }
        finally { window.Close(); }
    }

    private static async Task StartupWarning(List<string> report, string output)
    {
        foreach (var accept in new[] { false, true })
        {
            var path = Path.Combine(output, "profile-warning-preferences-" + Guid.NewGuid().ToString("N") + ".json");
            var preferences = new PrototypePreferences { Connected = true, SelectedEntityPath = "order-events/notifications", DeadLetter = true };
            preferences.Profiles[0].WarningMessage = "Saved production warning\nSecond line — investigate carefully.";
            var store = new PrototypePreferencesStore(path);
            Check(store.TrySave(preferences, out _) && store.TryLoad(out var restored, out _)
                && restored.Profiles[0].WarningMessage == preferences.Profiles[0].WarningMessage,
                "Profile warning text round-trips through an isolated preferences file", report);
            var window = new PrototypeWindow();
            window.ConfigureApplicationLifetime(true);
            try
            {
                Call(window, "InitializePreferences", path);
                await Respond(window.Show, true, accept, output, "profile-warning-startup-" + accept, report);
                Check(window.Workspace.IsConnected == accept,
                    "Saved connected profile requires startup warning approval: " + (accept ? "accepted" : "cancelled"), report);
                if (accept)
                    Check(window.Workspace.EntityPath == preferences.SelectedEntityPath && window.Workspace.IsDeadLetter
                        && window.Workspace.Messages.Count > 0 && window.Workspace.Messages.All(row => row.Source == preferences.SelectedEntityPath && row.IsDeadLetter),
                        "Accepting the startup warning restores the saved nondefault entity and DLQ tab", report);
            }
            finally { window.Close(); }
        }
    }

    private static async Task Respond(Action action, bool expectWarning, bool accept, string output, string name, List<string> report, Action? whilePending = null)
    {
        Exception? failure = null;
        var observed = false;
        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ProfileWarningWindow>().SingleOrDefault();
            try
            {
                observed = dialog is not null;
                if (dialog is null) return;
                Check(expectWarning, "A warning is displayed only when a new acknowledgment is required", report);
                whilePending?.Invoke();
                if (name.StartsWith("profile-warning-startup-", StringComparison.Ordinal) && dialog.Owner is PrototypeWindow startup)
                    Check(!startup.Workspace.IsConnected && startup.Workspace.Messages.Count == 0,
                        "Startup warning appears before the saved connection loads messages", report);
                ProofCapture.CheckBounds(dialog, ProofCapture.Descendants(dialog).OfType<Button>());
                ProofCapture.Save(dialog, output, name);
                Click(dialog, accept ? "ContinueButton" : "CancelButton");
            }
            catch (Exception error) { failure = error; dialog?.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        action();
        await Settle();
        if (failure is not null) throw failure;
        Check(observed == expectWarning, name + (expectWarning ? " displays the confirmation" : " avoids duplicate confirmation"), report);
    }

    private static Color Brush(object value) => value is SolidColorBrush brush ? brush.Color : throw new InvalidOperationException("Expected a solid theme brush.");
    private static void Call(PrototypeWindow window, string method, params object[] arguments) => typeof(PrototypeWindow)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task Complete(PrototypeWindow window)
    {
        await Settle();
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        Check(!window.Workspace.IsSearching, "Profile proof search completed", []);
        await Settle();
    }
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
