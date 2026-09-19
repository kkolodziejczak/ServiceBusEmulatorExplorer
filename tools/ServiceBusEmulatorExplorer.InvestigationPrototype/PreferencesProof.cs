using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class PreferencesProof
{
    public static async Task Exercise(PrototypeWindow owner, List<string> report, string output)
    {
        var path = Path.Combine(output, "preferences-proof-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new PrototypePreferencesStore(path);
        var seed = new PrototypePreferences();
        seed.Profiles[0].RuntimeConnection = "proof-runtime-secret-never-plaintext";
        seed.Profiles[0].AdministrationConnection = "proof-admin-secret-never-plaintext";
        seed.Profiles[0].ColorHex = "#007F80";
        seed.CloseToTray = true;
        seed.NotificationsEnabled = false;
        seed.RefreshIndex = 3;
        seed.TimeIndex = 2;
        seed.LogExpanded = false;
        seed.RefreshPaused = true;
        seed.Width = 1100; seed.Height = 800;
        var roots = PrototypeData.CreateTree();
        seed.SelectedEntityPath = roots.SelectMany(PrototypeData.Flatten).First(node => node.Kind == "Queue").Path;
        var topic = roots.SelectMany(PrototypeData.Flatten).First(node => node.Kind == "Topic");
        var child = topic.Children.First();
        var rules = new WatchRules();
        rules.SetGlobal(false, true);
        rules.SetGlobal(true, true);
        rules.SetIncluded(child.Path, false, roots);
        seed.Watches[seed.SelectedProfileId] = rules.Capture();
        Check(store.TrySave(seed, out var saveWarning), "Preferences save to an isolated proof file: " + saveWarning, report);
        var disk = File.ReadAllText(path);
        Check(!disk.Contains(seed.Profiles[0].RuntimeConnection) && !disk.Contains(seed.Profiles[0].AdministrationConnection),
            "Persisted connection strings do not appear as plaintext in the preference file", report);
        Check(store.TryLoad(out var loaded, out _) && loaded.Profiles[0].RuntimeConnection == seed.Profiles[0].RuntimeConnection
            && loaded.Profiles[0].AdministrationConnection == seed.Profiles[0].AdministrationConnection,
            "Protected connection strings round-trip for the current Windows user", report);
        var restoredRules = new WatchRules();
        restoredRules.Restore(loaded.Watches[loaded.SelectedProfileId]);
        var future = new EntityNode { Name = "future", Path = topic.Path + "/future", Kind = "Subscription" };
        topic.Children.Add(future);
        Check(!restoredRules.IsIncluded(child.Path, roots) && restoredRules.IsWatched(future.Path, true, roots),
            "Restored Watch inclusion keeps an unchecked child while covering future subscriptions", report);

        var first = Open(path);
        await Complete(first);
        Check(((ComboBox)first.FindName("AutoInterval")).SelectedIndex == 3 && ((ComboBox)first.FindName("TimeDisplaySelector")).SelectedIndex == 2
            && !((FrameworkElement)first.FindName("LogPanel")).IsVisible,
            "A fresh application restores refresh interval, time display and collapsed console", report);
        Check(Get<bool>(first, "closeToTray") && !Get<bool>(first, "notificationsEnabled")
            && ((PrototypeConnectionProfile)((ComboBox)first.FindName("ConnectionSelector")).SelectedItem).ColorHex == "#007F80",
            "A fresh application restores tray, notification and profile color preferences", report);
        Check(!Get<WatchRules>(first, "watchRules").IsIncluded(child.Path, first.Workspace.Roots),
            "Fresh application restores saved Watch tree inclusion", report);
        ((ComboBox)first.FindName("AutoInterval")).SelectedIndex = 2;
        ((ComboBox)first.FindName("TimeDisplaySelector")).SelectedIndex = 1;
        ((Button)first.FindName("LogToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var messageId = first.Workspace.Messages.First().MessageId;
        var query = MessageSearchQuery.QuoteLiteral(messageId);
        ((TextBox)first.FindName("SearchBox")).Text = query;
        Call(first, "BeginGlobalSearch", true);
        await Complete(first);
        first.Close();
        await Settle();
        var second = Open(path);
        await Complete(second);
        Check(((ComboBox)second.FindName("AutoInterval")).SelectedIndex == 2 && ((ComboBox)second.FindName("TimeDisplaySelector")).SelectedIndex == 1
            && ((FrameworkElement)second.FindName("LogPanel")).IsVisible,
            "Closing and reopening persists changed refresh, time and console settings", report);
        Check(second.Workspace.IsCorrelationSearch && second.Workspace.SearchByMessageId && second.Workspace.CorrelationQuery == query
            && second.Workspace.SelectedEntity?.Path == seed.SelectedEntityPath && Get<bool>(second, "paused")
            && second.Workspace.Messages.Count > 0 && second.Workspace.Messages.All(row => row.MessageId == messageId),
            "Closing and reopening restores nondefault entity, paused refresh and message-ID search with its original meaning", report);
        ProofCapture.Save(second, output, "preferences-restored");
        second.Close();
        await Settle();

        var corruptPath = Path.Combine(output, "preferences-corrupt-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(corruptPath, "{ invalid json");
        Check(!new PrototypePreferencesStore(corruptPath).TryLoad(out var defaults, out var warning)
            && !string.IsNullOrWhiteSpace(warning) && defaults.Profiles.Count > 0,
            "Corrupt preferences return safe defaults and a warning instead of throwing", report);
        owner.Activate();
    }

    private static PrototypeWindow Open(string path)
    {
        var window = new PrototypeWindow();
        window.ConfigureApplicationLifetime(true);
        Call(window, "InitializePreferences", path);
        window.Show();
        return window;
    }

    private static T Get<T>(PrototypeWindow window, string field) => (T)typeof(PrototypeWindow)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Call(PrototypeWindow window, string method, params object[] arguments) => typeof(PrototypeWindow)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static async Task Complete(PrototypeWindow window)
    {
        await Settle();
        for (var page = 0; window.Workspace.IsSearching && page < 1000; page++) window.Workspace.ScanNext();
        if (window.Workspace.IsSearching) throw new InvalidOperationException("Restored preference search exceeded bounded scan.");
        await Settle();
    }
    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Check(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("- PASS: " + message);
    }
}
