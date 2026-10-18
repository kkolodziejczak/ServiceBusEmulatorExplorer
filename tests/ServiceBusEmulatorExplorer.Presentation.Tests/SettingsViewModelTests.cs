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

    private sealed class NoSecrets : IConnectionSecrets
    {
        public string Runtime => string.Empty;

        public string Administration => string.Empty;
    }
}
