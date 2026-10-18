using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Characterizes Settings persistence through its controls only, so the contract survives moving the
/// window logic into a view model: failed saves roll back, pending saves block closing, copies are exact.
/// </summary>
[Collection("WPF presentation")]
public sealed class SettingsPersistenceContractTests
{
    private const string RuntimeConnection = "Endpoint=sb://runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";
    private const string AdministrationConnection = "Endpoint=sb://admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Failed_general_save_restores_the_persisted_value_and_a_retry_persists() => OnSta(() =>
    {
        var persistence = new ScriptedPersistence();
        var window = Open(persistence);
        try
        {
            ComboBox queue = Find<ComboBox>(window, "QueuePageSizeSelector");
            TextBlock status = Find<TextBlock>(window, "GeneralStatus");
            Button done = Find<Button>(window, "DoneButton");

            persistence.FailNext = true;
            queue.SelectedItem = 200;
            WaitUntil(() => status.Text.StartsWith("Could not save preferences:", StringComparison.Ordinal));

            Assert.Equal(25, queue.SelectedItem);
            Assert.Equal(25, window.CurrentPreferences.QueuePageSize);
            Assert.Empty(persistence.Saved);
            Assert.True(queue.IsEnabled);
            Assert.True(done.IsEnabled);

            queue.SelectedItem = 200;
            WaitUntil(() => status.Text == "General preferences saved.");

            Assert.Equal(200, Assert.Single(persistence.Saved).QueuePageSize);
            Assert.Equal(200, window.CurrentPreferences.QueuePageSize);
        }
        finally { Close(window, persistence); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Closing_while_a_save_is_pending_is_refused_until_the_save_finishes() => OnSta(() =>
    {
        var persistence = new ScriptedPersistence { HoldNext = true };
        var window = Open(persistence);
        try
        {
            ComboBox topic = Find<ComboBox>(window, "TopicPageSizeSelector");
            TextBlock status = Find<TextBlock>(window, "GeneralStatus");
            Button done = Find<Button>(window, "DoneButton");

            topic.SelectedItem = 25;
            WaitUntil(() => persistence.Held is not null);
            Assert.False(done.IsEnabled);
            Assert.Throws<ElementNotEnabledException>(() => AutomationPress.Press(done));

            window.Close();
            Drain();
            Assert.True(window.IsVisible);
            Assert.Equal("Saving preferences. Wait for the save to finish before closing.", status.Text);

            persistence.Held!.SetResult();
            WaitUntil(() => status.Text == "General preferences saved." && done.IsEnabled);
            Assert.Equal(25, Assert.Single(persistence.Saved).TopicPageSize);

            AutomationPress.Press(done);
            WaitUntil(() => !window.IsVisible);
        }
        finally { Close(window, persistence); }
    });

    [Theory]
    [InlineData("CopyRuntimeConnectionButton", RuntimeConnection, "Runtime connection string copied.")]
    [InlineData("CopyAdministrationConnectionButton", AdministrationConnection, "Administration connection string copied.")]
    [Trait("TestCategory", "UiRender")]
    public void Copying_a_connection_string_puts_the_exact_secret_on_the_clipboard(string buttonName, string expected, string message) => OnSta(() =>
    {
        var persistence = new ScriptedPersistence();
        var window = Open(persistence);
        string? previousClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        try
        {
            Find<TabControl>(window, "SettingsTabs").SelectedItem = Find<TabItem>(window, "ConnectionsTab");
            Drain();
            Button copy = Find<Button>(window, buttonName);
            WaitUntil(() => copy.IsEnabled);

            AutomationPress.Press(copy);
            WaitUntil(() => Find<TextBlock>(window, "ProfileStatus").Text == message);

            Assert.Equal(expected, Clipboard.GetText());
            Assert.Empty(persistence.Saved);
        }
        finally
        {
            if (previousClipboard is null) Clipboard.Clear(); else Clipboard.SetText(previousClipboard);
            Close(window, persistence);
        }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Copying_while_the_clipboard_is_unavailable_says_so_and_saves_nothing() => OnSta(() =>
    {
        var persistence = new ScriptedPersistence();
        var window = Open(persistence, _ => throw new ExternalException("The clipboard is held by another application."));
        try
        {
            Find<TabControl>(window, "SettingsTabs").SelectedItem = Find<TabItem>(window, "ConnectionsTab");
            Drain();
            Button copy = Find<Button>(window, "CopyRuntimeConnectionButton");
            WaitUntil(() => copy.IsEnabled);

            AutomationPress.Press(copy);
            WaitUntil(() => Find<TextBlock>(window, "ProfileStatus").Text == "Clipboard is unavailable. Try copying again.");

            Assert.True(copy.IsEnabled);
            Assert.Empty(persistence.Saved);
        }
        finally { Close(window, persistence); }
    });

    private static SettingsWindow Open(ScriptedPersistence persistence, Action<string>? copyText = null)
    {
        var window = new SettingsWindow(CreatePreferences(), persistence.SaveAsync, copyText: copyText);
        window.Show();
        WaitUntil(() => window.IsVisible && window.ActualWidth > 0);
        return window;
    }

    private static WorkspacePreferences CreatePreferences() => new()
    {
        Profiles = [new InvestigationProfile("primary",
            new ConnectionProfile("Primary workspace", RuntimeConnection, AdministrationConnection), "#0069FA")],
        SelectedProfileId = "primary",
        QueuePageSize = 25,
        TopicPageSize = 50,
        SubscriptionPageSize = 100
    };

    private static T Find<T>(Window window, string name) where T : class =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static void Close(SettingsWindow window, ScriptedPersistence persistence)
    {
        persistence.Held?.TrySetResult();
        if (!window.IsVisible) return;
        window.Close();
        WaitUntil(() => !window.IsVisible);
    }

    private static void Drain() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Settings did not reach the expected state.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The Settings contract exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class ScriptedPersistence
    {
        public bool FailNext { get; set; }
        public bool HoldNext { get; set; }
        public TaskCompletionSource? Held { get; private set; }
        public List<WorkspacePreferences> Saved { get; } = [];

        public async Task SaveAsync(WorkspacePreferences preferences)
        {
            if (HoldNext)
            {
                HoldNext = false;
                Held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await Held.Task;
            }

            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("synthetic persistence failure");
            }

            Saved.Add(preferences);
        }
    }
}
