using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class ConnectionBarViewModelTests
{
    [Fact]
    public void Requires_a_workspace() =>
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new ConnectionBarViewModel(null!)).ParamName);

    [Fact]
    public async Task The_button_connects_and_disconnects_and_the_dot_follows()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var bar = new ConnectionBarViewModel(fixture.Workspace);
        Assert.Equal(ConnectionHealth.Connected, bar.Health);
        Assert.Equal(fixture.Workspace.Status, bar.LastOperation);

        await bar.ToggleAsync();
        Assert.False(fixture.Workspace.IsConnected);
        Assert.Equal(ConnectionHealth.Disconnected, bar.Health);

        await bar.ToggleAsync();
        Assert.True(fixture.Workspace.IsConnected);
        Assert.Equal(ConnectionHealth.Connected, bar.Health);
    }

    [Fact]
    public async Task Connecting_shows_a_warning_dot_and_says_so_and_the_button_cancels_it()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var bar = new ConnectionBarViewModel(fixture.Workspace);
        await fixture.Workspace.DisconnectAsync();
        var probe = fixture.Gate();
        fixture.Peeks.BeforePeek = () => probe.Task;

        Task connecting = bar.ToggleAsync();

        Assert.True(fixture.Workspace.IsConnecting);
        Assert.Equal(ConnectionHealth.Warning, bar.Health);
        Assert.Equal("Connecting to Service Bus…", bar.LastOperation);
        await bar.ToggleAsync();
        await connecting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(fixture.Workspace.IsConnected || fixture.Workspace.IsConnecting);
    }

    [Fact]
    public async Task A_connection_with_a_readiness_warning_shows_the_warning_dot()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var bar = new ConnectionBarViewModel(fixture.Workspace);
        await fixture.Workspace.DisconnectAsync();
        fixture.DiscoveryComplete = false;

        await bar.ToggleAsync();

        Assert.True(fixture.Workspace.IsConnected);
        Assert.Equal(ConnectionHealth.Warning, bar.Health);
    }

    [Fact]
    public async Task Switching_profile_goes_through_the_workspace()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var bar = new ConnectionBarViewModel(fixture.Workspace);

        Assert.True(await bar.SwitchProfileAsync(fixture.Workspace.SelectedProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bar.SwitchProfileAsync(null!));
    }
}
