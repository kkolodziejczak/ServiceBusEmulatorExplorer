using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Settings;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class SettingsViewModelTests
{
    private static readonly WorkspacePreferences Start = new()
    {
        Profiles = [new InvestigationProfile("primary", new ConnectionProfile("Primary",
            "Endpoint=sb://runtime.example/;SharedAccessKeyName=Root;SharedAccessKey=secret",
            "Endpoint=sb://admin.example/;SharedAccessKeyName=Root;SharedAccessKey=secret"), "#0069FA")],
        SelectedProfileId = "primary",
        QueuePageSize = 25
    };

    [Fact]
    public void Requires_preferences_and_a_way_to_save_them()
    {
        Assert.Equal("preferences", Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(null!, _ => Task.CompletedTask, true, new NoSecrets(), _ => { }, [])).ParamName);
        Assert.Equal("saveAsync", Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(Start, null!, true, new NoSecrets(), _ => { }, [])).ParamName);
    }

    [Fact]
    public void Done_asks_the_view_to_close_when_nothing_is_saving()
    {
        var settings = new SettingsViewModel(Start, _ => Task.CompletedTask, true, new NoSecrets(), _ => { }, []);
        int closeRequests = 0;
        settings.CloseRequested += () => closeRequests++;

        Assert.True(settings.DoneCommand.CanExecute(null));
        settings.DoneCommand.Execute(null);

        Assert.Equal(1, closeRequests);
        Assert.True(settings.CanClose());
    }

    [Fact]
    public async Task While_any_tab_is_saving_Done_is_unavailable_and_closing_is_refused_with_a_reason()
    {
        var held = new TaskCompletionSource();
        var settings = new SettingsViewModel(Start, _ => held.Task, true, new NoSecrets(), _ => { }, []);
        var availability = new List<bool>();
        settings.DoneCommand.CanExecuteChanged += (_, _) => availability.Add(settings.DoneCommand.CanExecute(null));

        settings.General.QueuePageSize = 200;

        Assert.False(settings.DoneCommand.CanExecute(null));
        Assert.False(settings.CanClose());
        Assert.Equal("Saving preferences. Wait for the save to finish before closing.", settings.General.Status);

        held.SetResult();
        await Task.Yield();
        Assert.True(settings.CanClose());
        Assert.Equal([false, true], availability);
        Assert.Equal(200, settings.SavedPreferences.QueuePageSize);
    }

    [Fact]
    public void The_tabs_share_one_save_queue()
    {
        var held = new TaskCompletionSource();
        var settings = new SettingsViewModel(Start, _ => held.Task, true, new NoSecrets(), _ => { }, []);
        settings.Connections.Start();

        settings.General.QueuePageSize = 200;

        Assert.False(settings.Connections.SaveProfileCommand.CanExecute(null));
    }

    [Fact]
    public void Closing_without_unsaved_profile_edits_asks_nothing()
    {
        var settings = Started();
        settings.ConfirmUnsavedChanges = _ => throw new InvalidOperationException("Nothing should be asked.");

        Assert.True(settings.CanClose());
    }

    [Theory]
    [InlineData(UnsavedChangesChoice.Cancel, false)]
    [InlineData(UnsavedChangesChoice.Discard, true)]
    public void Closing_with_unsaved_profile_edits_asks_first(UnsavedChangesChoice choice, bool closes)
    {
        var settings = Started();
        var asked = new List<string>();
        settings.ConfirmUnsavedChanges = question => { asked.Add(question); return choice; };
        settings.Connections.ProfileName = "Payments";

        Assert.Equal(closes, settings.CanClose());
        Assert.Equal(["You have unsaved changes to Payments. What do you want to do?"], asked);
    }

    [Fact]
    public async Task Choosing_Save_keeps_the_window_open_until_every_edit_is_saved_then_closes_it()
    {
        var held = new TaskCompletionSource();
        var settings = Started(_ => held.Task);
        settings.ConfirmUnsavedChanges = _ => UnsavedChangesChoice.Save;
        int closeRequests = 0;
        settings.CloseRequested += () => closeRequests++;
        settings.Connections.WarningMessage = "Production";

        Assert.False(settings.CanClose());
        Assert.Equal(0, closeRequests);

        held.SetResult();
        await Task.Yield();
        Assert.Equal(1, closeRequests);
        Assert.Equal("Production", settings.SavedPreferences.Profiles[0].WarningMessage);
        Assert.True(settings.CanClose());
    }

    [Fact]
    public async Task Choosing_Save_with_an_invalid_profile_keeps_the_window_open()
    {
        var settings = Started();
        settings.ConfirmUnsavedChanges = _ => UnsavedChangesChoice.Save;
        int closeRequests = 0;
        settings.CloseRequested += () => closeRequests++;
        settings.Connections.ProfileName = string.Empty;

        Assert.False(settings.CanClose());
        await Task.Yield();

        Assert.Equal(0, closeRequests);
        Assert.NotEqual(string.Empty, settings.Connections.Status);
    }

    private static SettingsViewModel Started(Func<WorkspacePreferences, Task>? saveAsync = null)
    {
        var secrets = new ShownSecrets();
        var settings = new SettingsViewModel(Start, saveAsync ?? (_ => Task.CompletedTask), true, secrets, _ => { }, []);
        settings.Connections.ProfileLoaded += secrets.Show;
        settings.Connections.Start();
        return settings;
    }

    private sealed class NoSecrets : IConnectionSecrets
    {
        public string Runtime => string.Empty;

        public string Administration => string.Empty;
    }

    /// <summary>Stands in for the window's password fields, which show each loaded profile's connection strings.</summary>
    private sealed class ShownSecrets : IConnectionSecrets
    {
        public string Runtime { get; private set; } = string.Empty;

        public string Administration { get; private set; } = string.Empty;

        public void Show(ProfileDraft draft)
        {
            Runtime = draft.RuntimeConnectionString;
            Administration = draft.AdministrationConnectionString;
        }
    }
}
