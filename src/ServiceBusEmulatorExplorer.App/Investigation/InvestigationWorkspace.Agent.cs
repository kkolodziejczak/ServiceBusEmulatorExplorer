using ServiceBusEmulatorExplorer.App.Agent;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>Agent (MCP) reads of the live session. Call on the UI thread; see specs/agent-access.md.</summary>
public sealed partial class InvestigationWorkspace
{
    private static readonly TimeSpan AgentPeekTimeout = TimeSpan.FromSeconds(30);
    private CancellationTokenSource agentOperations = new();

    public AgentArrivalJournal AgentArrivals { get; } = new();

    public bool AgentAccessAllowed => SelectedProfile.AllowAgentAccess;

    public AgentSnapshot CaptureAgentSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = SelectedProfile;
        if (!profile.AllowAgentAccess) return AgentSnapshot.Blocked(now);
        return new AgentSnapshot(
            Allowed: true,
            CapturedAtUtc: now,
            ProfileName: profile.Connection.Name,
            IsEmulator: profile.Connection.RuntimeConnectionString.Contains("UseDevelopmentEmulator=true", StringComparison.OrdinalIgnoreCase),
            Connected: IsConnected,
            Health: HealthText,
            HealthDetail: HealthDetail,
            ConnectionGeneration: generation,
            SelectedEntityPath: Search.IsActive ? null : Browse.SelectedEntity?.Path,
            SelectedBucket: Search.IsActive || Browse.SelectedEntity is null ? null : Browse.IsDeadLetter ? MessageBucket.DeadLetter : MessageBucket.Active,
            SearchActive: Search.IsActive,
            Focused: Inspector.Current,
            Visible: Surface.Messages.Select(row => row.Delivery).ToArray(),
            Discovery: Browse.DiscoverySnapshot,
            WatchRules: CurrentWatchRules().ToArray(),
            PendingWatchArrivals: Watch.PendingArrivals.Count);
    }

    public async Task<IReadOnlyList<MessageDelivery>> PeekForAgentAsync(string entityPath, MessageBucket bucket, int take,
        long? fromSequenceNumber, CancellationToken cancellationToken)
    {
        var profile = SelectedProfile;
        if (!profile.AllowAgentAccess) throw new AgentAccessBlockedException();
        var current = session ?? throw new InvalidOperationException("The app is not connected. Ask the user to connect first.");
        var observation = Browse.DiscoverySnapshot?.Entities.FirstOrDefault(entity => string.Equals(PathOf(entity.Entity), entityPath?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown entity '{entityPath}'. Use a path from list_entities.");
        if (observation.Entity.Kind == EntityKind.Topic)
            throw new InvalidOperationException("Topics cannot be peeked. Peek one of the topic's subscriptions.");

        var address = new EntityAddress(observation.Entity.Kind, observation.Entity.Name, observation.Entity.TopicName);
        long started = generation;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, agentOperations.Token);
        operation.CancelAfter(AgentPeekTimeout);
        var messages = await current.Messages.PeekMessagesAsync(address, bucket, take, fromSequenceNumber, operation.Token);

        // The user may have switched profiles, disconnected or blocked agents while the broker answered.
        if (started != generation || SelectedProfile.Id != profile.Id || !SelectedProfile.AllowAgentAccess)
            throw new AgentAccessBlockedException();
        return messages.Select(message => new MessageDelivery(
            new DeliveryIdentity(started, address, bucket, message.SequenceNumber), message)).ToArray();
    }

    public AgentArrivalPage ReadAgentArrivals(string? cursor, int limit) =>
        AgentAccessAllowed ? AgentArrivals.Read(cursor, limit) : throw new AgentAccessBlockedException();

    internal void RecordAgentArrival(MessageDelivery delivery)
    {
        // Arrivals seen while a profile blocks agents are never retained for them (DEC-025). A Watch page
        // can finish while a disconnect or profile switch is in progress; it belongs to an invalidated
        // connection and must not refill the journal that the switch just cleared.
        if (session is not null && delivery.Identity.ConnectionGeneration == generation && SelectedProfile.AllowAgentAccess)
            AgentArrivals.Append(delivery);
    }

    private void ResetAgentState()
    {
        agentOperations.Cancel();
        agentOperations.Dispose();
        agentOperations = new CancellationTokenSource();
        AgentArrivals.Clear();
    }

    private static string PathOf(DiscoveredEntity entity) =>
        entity.Kind == EntityKind.Subscription ? $"{entity.TopicName}/{entity.Name}" : entity.Name;
}
