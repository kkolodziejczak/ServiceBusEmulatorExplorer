using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal sealed class ScenarioWorkspacePreferencesStore(WorkspacePreferences preferences) : IWorkspacePreferencesStore
{
    private WorkspacePreferences current = preferences;

    public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PreferencesLoadResult(current));

    public Task SaveAsync(WorkspacePreferences value, CancellationToken cancellationToken)
    {
        current = value;
        return Task.CompletedTask;
    }
}

internal sealed class ScenarioClientFactory : IServiceBusClientFactory
{
    public bool SupportsRuntimeCounts => true;
    public ServiceBusAdministrationClient AdministrationClient => null!;
    public ServiceBusClient RuntimeClient => null!;

    public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ScenarioEntityBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
{
    public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult(snapshot);
}

internal sealed class ScenarioMessageService(IReadOnlyList<ExplorerMessage> messages) : IServiceBusMessageService
{
    public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
        EntityAddress address,
        MessageBucket bucket,
        int take,
        long? fromSequenceNumber,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExplorerMessage> result = bucket == MessageBucket.Active
            && address.Kind == EntityKind.Subscription
            && address.TopicName == "order-events"
            ? messages
                .Where(message => message.MessageId.EndsWith(address.Name, StringComparison.Ordinal)
                    || (address.Name == "fulfillment" && message.MessageId == "order-10482-dispatched"))
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                .OrderBy(message => message.SequenceNumber)
                .Take(take)
                .ToArray()
            : [];
        return Task.FromResult(result);
    }

    public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
}
