using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Settings 70: Save profile only on Connections, the Modified pill on edited profiles and inside the Connections tab,
/// and the Save changes? question before closing with unsaved profile edits.
/// </summary>
[Collection("WPF presentation")]
public sealed class SettingsUnsavedEditsTests
{
    private const string Runtime = "Endpoint=sb://primary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";
    private const string Administration = "Endpoint=sb://primary-admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Save_profile_shows_only_on_the_Connections_tab() => OnSta(() =>
    {
        var window = Open([], _ => UnsavedChangesChoice.Cancel);
        try
        {
            Button saveProfile = Find<Button>(window, "SaveProfileButton");
            Button done = Find<Button>(window, "DoneButton");

            Assert.Equal(Visibility.Collapsed, saveProfile.Visibility);
            Select(window, "ConnectionsTab");
            Assert.Equal(Visibility.Visible, saveProfile.Visibility);
            Select(window, "LicencesTab");
            Assert.Equal(Visibility.Collapsed, saveProfile.Visibility);
            Assert.Equal(Visibility.Visible, done.Visibility);
        }
        finally { Dismiss(window); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void An_edited_profile_shows_Modified_on_its_row_and_inside_the_Connections_tab() => OnSta(() =>
    {
        var window = Open([], _ => UnsavedChangesChoice.Cancel);
        try
        {
            Select(window, "ConnectionsTab");
            TabItem tab = Find<TabItem>(window, "ConnectionsTab");
            FrameworkElement tabBadge = Find<FrameworkElement>(window, "ConnectionsModifiedBadge");
            ListBox profiles = Find<ListBox>(window, "ProfilesList");
            Assert.Equal(Visibility.Collapsed, tabBadge.Visibility);
            Assert.Equal("Connections", AutomationProperties.GetName(tab));

            profiles.SelectedIndex = 1;
            Settle();
            Find<PasswordBox>(window, "RuntimeConnection").Password = Runtime;
            Settle();

            var row = (ListBoxItem)profiles.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.Equal("Modified", AutomationProperties.GetItemStatus(row));
            Assert.Equal(string.Empty, AutomationProperties.GetItemStatus((ListBoxItem)profiles.ItemContainerGenerator.ContainerFromIndex(0)));
            Assert.Equal(Visibility.Visible, tabBadge.Visibility);
            Assert.Same(tab, Ancestor<TabItem>(tabBadge));
            Assert.Equal("Modified", AutomationProperties.GetItemStatus(tab));
            VisualBaseline.AssertMatches(window, "settings-unsaved-connections");

            Find<PasswordBox>(window, "RuntimeConnection").Password = "Endpoint=sb://secondary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret";
            Settle();
            Assert.Equal(Visibility.Collapsed, tabBadge.Visibility);

            Find<PasswordBox>(window, "AdministrationConnection").Password = Administration;
            Settle();
            Assert.Equal("Modified", AutomationProperties.GetItemStatus(row));
        }
        finally { Dismiss(window); }
    });

    [Theory]
    [Trait("TestCategory", "UiRender")]
    [InlineData(UnsavedChangesChoice.Cancel, true)]
    [InlineData(UnsavedChangesChoice.Discard, false)]
    public void Done_with_unsaved_edits_asks_first(UnsavedChangesChoice choice, bool staysOpen) => OnSta(() =>
    {
        var saved = new List<WorkspacePreferences>();
        var asked = new List<string>();
        var window = Open(saved, question => { asked.Add(question); return choice; });
        try
        {
            Select(window, "ConnectionsTab");
            Find<TextBox>(window, "ProfileName").Text = "Payments";
            Select(window, "GeneralTab");

            AutomationPress.Press(Find<Button>(window, "DoneButton"));
            Settle();

            Assert.Equal(["You have unsaved changes to Payments. What do you want to do?"], asked);
            Assert.Equal(staysOpen, window.IsVisible);
            Assert.Empty(saved);
        }
        finally { Dismiss(window); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Choosing_Save_saves_every_edit_and_closes() => OnSta(() =>
    {
        var saved = new List<WorkspacePreferences>();
        var window = Open(saved, _ => UnsavedChangesChoice.Save);
        try
        {
            Select(window, "ConnectionsTab");
            Find<TextBox>(window, "ConnectionWarning").Text = "Production";

            window.Close();
            WaitUntil(() => !window.IsVisible);

            Assert.Equal("Production", Assert.Single(saved).Profiles[0].WarningMessage);
        }
        finally { Dismiss(window); }
    });

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Choosing_Save_with_an_invalid_profile_shows_it_on_the_Connections_tab() => OnSta(() =>
    {
        var window = Open([], _ => UnsavedChangesChoice.Save);
        try
        {
            Select(window, "ConnectionsTab");
            Find<TextBox>(window, "ProfileName").Text = string.Empty;
            Select(window, "GeneralTab");

            AutomationPress.Press(Find<Button>(window, "DoneButton"));
            WaitUntil(() => Find<TextBlock>(window, "ProfileStatus").Text.Length > 0);

            Assert.Equal("Profile name is required.", Find<TextBlock>(window, "ProfileStatus").Text);
            Assert.True(window.IsVisible);
            Assert.Same(Find<TabItem>(window, "ConnectionsTab"), Find<TabControl>(window, "SettingsTabs").SelectedItem);
        }
        finally { Dismiss(window); }
    });

    [Theory]
    [Trait("TestCategory", "UiRender")]
    [InlineData("SaveChangesSaveButton", UnsavedChangesChoice.Save)]
    [InlineData("SaveChangesDiscardButton", UnsavedChangesChoice.Discard)]
    [InlineData("SaveChangesCancelButton", UnsavedChangesChoice.Cancel)]
    public void The_question_returns_the_pressed_answer(string button, UnsavedChangesChoice expected) => OnSta(() =>
    {
        var dialog = new SaveChangesWindow("You have unsaved changes to Secondary workspace. What do you want to do?", "#7540BF");
        dialog.ContentRendered += (_, _) =>
        {
            if (button == "SaveChangesSaveButton") VisualBaseline.AssertMatches(dialog, "settings-save-changes");
            Button pressed = Assert.IsAssignableFrom<Button>(Descendants(dialog).OfType<Button>()
                .Single(candidate => AutomationProperties.GetAutomationId(candidate) == button));
            AutomationPress.Press(pressed);
        };

        dialog.ShowDialog();

        Assert.Equal(expected, dialog.Choice);
    });

    private static SettingsWindow Open(List<WorkspacePreferences> saved, Func<string, UnsavedChangesChoice> confirm)
    {
        var preferences = new WorkspacePreferences
        {
            Profiles =
            [
                new InvestigationProfile("primary", new ConnectionProfile("Primary workspace", Runtime, Administration), "#0069FA"),
                new InvestigationProfile("secondary", new ConnectionProfile("Secondary workspace",
                    "Endpoint=sb://secondary-runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret",
                    "Endpoint=sb://secondary-admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret"), "#7540BF")
            ],
            SelectedProfileId = "primary"
        };
        var window = new SettingsWindow(preferences, next => { saved.Add(next); return Task.CompletedTask; },
            confirmUnsavedChanges: confirm);
        window.Show();
        WaitUntil(() => window.IsVisible && window.ActualWidth > 0);
        return window;
    }

    private static void Select(Window window, string tab)
    {
        Find<TabControl>(window, "SettingsTabs").SelectedItem = Find<TabItem>(window, tab);
        Settle();
    }

    /// <summary>Hides a window that may still refuse to close, because its question answers Cancel or Save.</summary>
    private static void Dismiss(Window window)
    {
        if (window.IsVisible) window.Hide();
    }

    private static T Find<T>(Window window, string name) where T : class =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Settle() =>
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The Settings unsaved-edits test exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
