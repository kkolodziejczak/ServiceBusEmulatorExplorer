using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WindowCloseViewModelTests
{
    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new WindowCloseViewModel(null!)).ParamName);

    [Fact]
    public async Task The_window_hides_in_the_tray_only_when_asked_to_and_able_to()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var close = new WindowCloseViewModel(fixture.Workspace);
        await fixture.Workspace.ApplyPreferencesAsync(fixture.Workspace.Preferences with { CloseToTray = true });

        Assert.True(close.HidesToTray(exitRequested: false, trayAvailable: true));
        Assert.False(close.HidesToTray(exitRequested: true, trayAvailable: true));
        Assert.False(close.HidesToTray(exitRequested: false, trayAvailable: false));

        await fixture.Workspace.ApplyPreferencesAsync(fixture.Workspace.Preferences with { CloseToTray = false });
        Assert.False(close.HidesToTray(exitRequested: false, trayAvailable: true));
    }

    [Fact]
    public async Task Drafts_are_only_discarded_after_confirmation()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var close = new WindowCloseViewModel(fixture.Workspace);
        int asked = 0;
        bool answer = false;
        fixture.Workspace.ConfirmDiscard = () => { asked++; return Task.FromResult(answer); };

        Assert.True(await close.ConfirmCloseAsync());
        Assert.Equal(0, asked);

        fixture.Focus(0);
        fixture.Workspace.Inspector.Document.Text = "{\"draft\":true}";
        Assert.False(await close.ConfirmCloseAsync());
        answer = true;
        Assert.True(await close.ConfirmCloseAsync());
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task Shutting_down_remembers_the_window_size_and_disconnects()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var close = new WindowCloseViewModel(fixture.Workspace);
        bool connectedWhileSaving = false;
        fixture.Store.BeforeSave = () => { connectedWhileSaving = fixture.Workspace.IsConnected; return Task.CompletedTask; };

        await close.ShutDownAsync(1234, 777);

        Assert.True(connectedWhileSaving);

        Assert.Equal(1234, fixture.Store.Saved.WindowWidth);
        Assert.Equal(777, fixture.Store.Saved.WindowHeight);
        Assert.False(fixture.Workspace.IsConnected);
    }
}
