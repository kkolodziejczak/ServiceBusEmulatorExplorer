using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Pins Settings behaviour that moving the window logic into view models must keep (DEC-053): opening is inert,
/// profile drafts survive switching, new connections get a free name, and saves are serialized and block closing.
/// Acts only through controls and UI Automation, so the view-model extraction must pass it unchanged.
/// </summary>
[Collection("WPF presentation")]
public sealed class SettingsBehaviourContractTests
{
    private const string PrimaryRuntime = "Endpoint=sb://primary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";
    private const string PrimaryAdministration = "Endpoint=sb://primary-admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Opening_settings_saves_nothing_and_shows_the_selected_profile() => OnSta(() =>
    {
        var persistence = new HeldPersistence();
        var window = Open(persistence);
        try
        {
            SelectConnections(window);

            Assert.Empty(persistence.Requested);
            Assert.Equal("Primary workspace", Find<TextBox>(window, "ProfileName").Text);
            Assert.Equal(PrimaryRuntime, Find<PasswordBox>(window, "RuntimeConnection").Password);
            Assert.Equal(string.Empty, Find<TextBlock>(window, "GeneralStatus").Text);
            Assert.Equal(string.Empty, Find<TextBlock>(window, "ProfileStatus").Text);
        }
        finally { Close(window, persistence); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Switching_profiles_keeps_the_unsaved_draft_of_the_profile_you_left() => OnSta(() =>
    {
        var persistence = new HeldPersistence();
        var window = Open(persistence);
        try
        {
            SelectConnections(window);
            ListBox profiles = Find<ListBox>(window, "ProfilesList");
            Find<TextBox>(window, "ProfileName").Text = "  Primary renamed  ";
            Find<PasswordBox>(window, "RuntimeConnection").Password = "Endpoint=sb://edited.example/;SharedAccessKeyName=Root;SharedAccessKey=edited";
            Find<TextBox>(window, "ConnectionWarning").Text = "Edited warning";

            profiles.SelectedIndex = 1;
            Drain();
            Assert.Equal("Secondary workspace", Find<TextBox>(window, "ProfileName").Text);
            profiles.SelectedIndex = 0;
            Drain();

            Assert.Equal("Primary renamed", Find<TextBox>(window, "ProfileName").Text);
            Assert.Equal("Endpoint=sb://edited.example/;SharedAccessKeyName=Root;SharedAccessKey=edited",
                Find<PasswordBox>(window, "RuntimeConnection").Password);
            Assert.Equal(PrimaryAdministration, Find<PasswordBox>(window, "AdministrationConnection").Password);
            Assert.Equal("Edited warning", Find<TextBox>(window, "ConnectionWarning").Text);
            Assert.Empty(persistence.Requested);
        }
        finally { Close(window, persistence); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Adding_a_connection_picks_the_first_free_name_selects_it_and_focuses_its_name() => OnSta(() =>
    {
        var persistence = new HeldPersistence();
        var window = Open(persistence, firstProfileName: "connection 1");
        try
        {
            SelectConnections(window);
            ListBox profiles = Find<ListBox>(window, "ProfilesList");
            TextBox name = Find<TextBox>(window, "ProfileName");

            AutomationPress.Press(Find<Button>(window, "AddConnectionButton"));

            Assert.Equal(3, profiles.Items.Count);
            Assert.Equal(2, profiles.SelectedIndex);
            Assert.Equal("Connection 2", name.Text);
            Assert.Same(name, FocusManager.GetFocusedElement(window));
            Assert.Equal(name.Text.Length, name.SelectionLength);
            Assert.Equal(string.Empty, Find<PasswordBox>(window, "RuntimeConnection").Password);
            Assert.Empty(persistence.Requested);
        }
        finally { Close(window, persistence); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Closing_while_a_profile_save_is_pending_is_refused_until_it_finishes() => OnSta(() =>
    {
        var persistence = new HeldPersistence();
        var window = Open(persistence);
        try
        {
            SelectConnections(window);
            Button done = Find<Button>(window, "DoneButton");
            Button saveProfile = Find<Button>(window, "SaveProfileButton");

            AutomationPress.Press(saveProfile);
            WaitUntil(() => persistence.Requested.Count == 1);
            Assert.False(done.IsEnabled);
            Assert.False(saveProfile.IsEnabled);
            Assert.False(Find<TextBox>(window, "ProfileName").IsEnabled);

            window.Close();
            Drain();
            Assert.True(window.IsVisible);
            Assert.Equal("Saving preferences. Wait for the save to finish before closing.", Find<TextBlock>(window, "GeneralStatus").Text);

            persistence.Release(0);
            WaitUntil(() => Find<TextBlock>(window, "ProfileStatus").Text == "Profile saved. No connection was attempted." && done.IsEnabled);
            Assert.True(saveProfile.IsEnabled);
            Assert.True(Find<TextBox>(window, "ProfileName").IsEnabled);
        }
        finally { Close(window, persistence); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void A_general_change_during_a_profile_save_waits_for_it_keeps_both_changes_and_blocks_saving_until_done() => OnSta(() =>
    {
        var persistence = new HeldPersistence();
        var window = Open(persistence);
        try
        {
            SelectConnections(window);
            Button done = Find<Button>(window, "DoneButton");
            Button saveProfile = Find<Button>(window, "SaveProfileButton");
            ComboBox queue = Find<ComboBox>(window, "QueuePageSizeSelector");

            Find<TextBox>(window, "ConnectionWarning").Text = "Saved with the profile";
            AutomationPress.Press(saveProfile);
            WaitUntil(() => persistence.Requested.Count == 1);
            Assert.True(queue.IsEnabled);
            queue.SelectedItem = 200;
            Drain();
            Assert.Single(persistence.Requested);

            persistence.Release(0);
            WaitUntil(() => persistence.Requested.Count == 2);
            Assert.Equal("Saved with the profile", persistence.Requested[1].Profiles.Single(profile => profile.Id == "primary").WarningMessage);
            Assert.Equal(200, persistence.Requested[1].QueuePageSize);
            Assert.False(done.IsEnabled);
            Assert.False(saveProfile.IsEnabled);

            persistence.Release(1);
            WaitUntil(() => Find<TextBlock>(window, "GeneralStatus").Text == "General preferences saved." && done.IsEnabled);
            Assert.True(saveProfile.IsEnabled);
            Assert.Equal(200, window.CurrentPreferences.QueuePageSize);
        }
        finally { Close(window, persistence); }
    });

    private static SettingsWindow Open(HeldPersistence persistence, string firstProfileName = "Primary workspace")
    {
        var preferences = new WorkspacePreferences
        {
            Profiles =
            [
                new InvestigationProfile("primary", new ConnectionProfile(firstProfileName, PrimaryRuntime, PrimaryAdministration), "#0069FA"),
                new InvestigationProfile("secondary", new ConnectionProfile("Secondary workspace",
                    "Endpoint=sb://secondary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret",
                    "Endpoint=sb://secondary-admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret"), "#7540BF")
            ],
            SelectedProfileId = "primary",
            QueuePageSize = 25,
            TopicPageSize = 50,
            SubscriptionPageSize = 100
        };
        var window = new SettingsWindow(preferences, persistence.SaveAsync, confirmUnsavedChanges: _ => UnsavedChangesChoice.Discard);
        window.Show();
        WaitUntil(() => window.IsVisible && window.ActualWidth > 0);
        return window;
    }

    private static void SelectConnections(Window window)
    {
        Find<TabControl>(window, "SettingsTabs").SelectedItem = Find<TabItem>(window, "ConnectionsTab");
        Drain();
    }

    private static T Find<T>(Window window, string name) where T : class =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static void Close(SettingsWindow window, HeldPersistence persistence)
    {
        persistence.ReleaseAll();
        if (!window.IsVisible) return;
        WaitUntil(() => window.FindName("DoneButton") is Button { IsEnabled: true });
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The Settings behaviour contract exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Holds every save until the test releases it, so overlapping saves can be observed.</summary>
    private sealed class HeldPersistence
    {
        private readonly List<TaskCompletionSource> held = [];

        public List<WorkspacePreferences> Requested { get; } = [];

        public Task SaveAsync(WorkspacePreferences preferences)
        {
            Requested.Add(preferences);
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            held.Add(pending);
            return pending.Task;
        }

        public void Release(int index) => held[index].TrySetResult();

        public void ReleaseAll()
        {
            foreach (TaskCompletionSource pending in held) pending.TrySetResult();
        }
    }
}
