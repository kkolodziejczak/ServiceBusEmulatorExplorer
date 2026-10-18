using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>Licences 69: Settings shows every bundled component's licence and copies it exactly.</summary>
[Collection("WPF presentation")]
public sealed class SettingsLicencesTests
{
    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void Licences_tab_lists_every_component_and_copies_the_selected_licence() => OnSta(() =>
    {
        var copied = new List<string>();
        var window = new SettingsWindow(Preferences(), _ => Task.CompletedTask, copyText: copied.Add);
        try
        {
            window.Show();
            Settle(window);
            Select(window, "LicencesTab");
            var list = (ListBox)window.FindName("LicencesList")!;
            var text = (TextBox)window.FindName("LicenceText")!;
            var status = (TextBlock)window.FindName("LicenceStatus")!;

            Assert.Equal(ThirdPartyNotices.Components.Select(component => component.Name), list.Items.Cast<ThirdPartyComponent>().Select(component => component.Name));
            Assert.Equal("Selawik", ((ThirdPartyComponent)list.SelectedItem).Name);
            Assert.Contains("Reserved Font Name Selawik", text.Text, StringComparison.Ordinal);
            Assert.True(text.IsReadOnly);
            VisualBaseline.AssertMatches(window, "settings-licences");

            list.SelectedItem = ThirdPartyNotices.Components.Single(component => component.Name == "AvalonEdit");
            Settle(window);
            AutomationPress.Press((Button)window.FindName("CopyLicenceButton")!);

            string expected = ThirdPartyNotices.Components.Single(component => component.Name == "AvalonEdit").Notice;
            Assert.Equal([expected], copied);
            Assert.Equal(expected, text.Text);
            Assert.Equal("AvalonEdit licence copied.", status.Text);
            VisualBaseline.AssertMatches(window, "settings-licences-copied");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Copying_a_licence_reports_an_unavailable_clipboard() => OnSta(() =>
    {
        var window = new SettingsWindow(Preferences(), _ => Task.CompletedTask,
            copyText: _ => throw new ExternalException("clipboard locked"));
        try
        {
            window.Show();
            Settle(window);
            Select(window, "LicencesTab");

            AutomationPress.Press((Button)window.FindName("CopyLicenceButton")!);

            Assert.Equal("Clipboard is unavailable. Try copying again.", ((TextBlock)window.FindName("LicenceStatus")!).Text);
        }
        finally
        {
            window.Close();
        }
    });

    private static WorkspacePreferences Preferences()
    {
        var profile = new InvestigationProfile("licences", new ConnectionProfile("Licences proof", "runtime", "admin"), "#0069FA");
        return new WorkspacePreferences { Profiles = [profile], SelectedProfileId = profile.Id };
    }

    private static void Select(Window window, string tabName)
    {
        ((TabItem)window.FindName(tabName)!).IsSelected = true;
        Settle(window);
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Licences proof exceeded its 30-second bound.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
