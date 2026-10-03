using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Agent;

/// <summary>Runs agent reads on the workspace's UI thread, where its observable state lives.</summary>
public sealed class WorkspaceAgentStateSource(InvestigationWorkspace workspace, Dispatcher dispatcher) : IAgentStateSource
{
    public Task<AgentSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(workspace.CaptureAgentSnapshot, DispatcherPriority.Normal, cancellationToken).Task;

    public async Task<IReadOnlyList<MessageDelivery>> PeekAsync(string entityPath, MessageBucket bucket, int take,
        long? fromSequenceNumber, CancellationToken cancellationToken) =>
        await await dispatcher.InvokeAsync(
            () => workspace.PeekForAgentAsync(entityPath, bucket, take, fromSequenceNumber, cancellationToken),
            DispatcherPriority.Normal, cancellationToken);

    public Task<AgentArrivalPage> ReadArrivalsAsync(string? cursor, int limit, CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => workspace.ReadAgentArrivals(cursor, limit), DispatcherPriority.Normal, cancellationToken).Task;
}
