using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Agent;

/// <summary>
/// What agents may see of the live session, captured on the UI thread in one step so state and
/// permission are read together. Contains no connection strings or tokens.
/// </summary>
public sealed record AgentSnapshot(
    bool Allowed,
    DateTimeOffset CapturedAtUtc,
    string ProfileName = "",
    bool IsEmulator = false,
    bool Connected = false,
    string Health = "",
    string HealthDetail = "",
    long ConnectionGeneration = 0,
    string? SelectedEntityPath = null,
    MessageBucket? SelectedBucket = null,
    bool SearchActive = false,
    MessageDelivery? Focused = null,
    IReadOnlyList<MessageDelivery>? Visible = null,
    EntityDiscoverySnapshot? Discovery = null,
    IReadOnlyList<WatchPreference>? WatchRules = null,
    int PendingWatchArrivals = 0)
{
    public static AgentSnapshot Blocked(DateTimeOffset now) => new(false, now);
}

/// <summary>Thrown when the active profile does not allow agent access (DEC-025).</summary>
public sealed class AgentAccessBlockedException() : Exception(AgentAccessBlockedException.UserMessage)
{
    public const string UserMessage = "Agent access is turned off for the current connection profile.";
}

public interface IAgentStateSource
{
    Task<AgentSnapshot> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>Non-consuming peek for an agent. Throws <see cref="AgentAccessBlockedException"/> when blocked
    /// before or after the broker call, and <see cref="InvalidOperationException"/> when not connected or the
    /// entity is unknown.</summary>
    Task<IReadOnlyList<MessageDelivery>> PeekAsync(string entityPath, MessageBucket bucket, int take, long? fromSequenceNumber,
        CancellationToken cancellationToken);

    /// <summary>Reads Watch arrivals; throws <see cref="AgentAccessBlockedException"/> when blocked.</summary>
    Task<AgentArrivalPage> ReadArrivalsAsync(string? cursor, int limit, CancellationToken cancellationToken);
}
