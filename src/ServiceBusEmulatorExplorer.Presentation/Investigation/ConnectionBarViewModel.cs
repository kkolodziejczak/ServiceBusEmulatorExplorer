using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>How the connection is doing, shown by the connection button's dot.</summary>
public enum ConnectionHealth
{
    Connected,
    Warning,
    Disconnected
}

/// <summary>The connection bar: the chosen profile, the connection's health, the last operation, and connecting.</summary>
public sealed class ConnectionBarViewModel
{
    private readonly InvestigationWorkspace workspace;

    public ConnectionBarViewModel(InvestigationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        this.workspace = workspace;
    }

    /// <summary>Connecting and a connection with a readiness warning show as a warning.</summary>
    public ConnectionHealth Health => workspace.IsConnecting || workspace.HealthText == "Warning" ? ConnectionHealth.Warning
        : workspace.IsConnected ? ConnectionHealth.Connected : ConnectionHealth.Disconnected;

    /// <summary>The status line: connecting, or the latest operation.</summary>
    public string LastOperation => workspace.IsConnecting ? "Connecting to Service Bus…" : workspace.Status;

    /// <summary>Disconnects while connected or connecting, otherwise connects.</summary>
    public Task ToggleAsync() => workspace.IsConnected || workspace.IsConnecting ? workspace.DisconnectAsync() : workspace.ConnectAsync();

    public Task<bool> SwitchProfileAsync(InvestigationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return workspace.SwitchProfileAsync(profile);
    }
}
