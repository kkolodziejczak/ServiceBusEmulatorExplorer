using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record BrokerSession(
    IServiceBusClientFactory Factory,
    IInvestigationEntityBrowser Browser,
    IServiceBusMessageService Messages,
    EntityDiscoverySnapshot Snapshot,
    string? ReadinessWarning) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

public sealed class BrokerConnectionWorkflow
{
    private readonly Func<IServiceBusClientFactory> createFactory;
    private readonly Func<IServiceBusClientFactory, IInvestigationEntityBrowser> createBrowser;
    private readonly Func<IServiceBusClientFactory, IServiceBusMessageService> createMessages;

    public BrokerConnectionWorkflow(
        Func<IServiceBusClientFactory> createFactory,
        Func<IServiceBusClientFactory, IInvestigationEntityBrowser> createBrowser,
        Func<IServiceBusClientFactory, IServiceBusMessageService> createMessages)
    {
        this.createFactory = createFactory;
        this.createBrowser = createBrowser;
        this.createMessages = createMessages;
    }

    public async Task<BrokerSession> ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        var factory = createFactory();
        try
        {
            await factory.ConnectAsync(profile, cancellationToken);
            var browser = createBrowser(factory);
            var messages = createMessages(factory);
            var snapshot = await browser.DiscoverAsync(cancellationToken);
            var probe = snapshot.Entities.FirstOrDefault(entity => entity.Entity.Kind != EntityKind.Topic);
            string? warning = null;
            if (probe is null)
                warning = "Administration is available; runtime access cannot be verified without a queue or subscription.";
            else
                await messages.PeekMessagesAsync(new(probe.Entity.Kind, probe.Entity.Name, probe.Entity.TopicName),
                    MessageBucket.Active, 1, null, cancellationToken);
            if (!snapshot.IsComplete) warning = "Connected with incomplete namespace discovery. Refresh to retry unavailable sources.";
            cancellationToken.ThrowIfCancellationRequested();
            return new(factory, browser, messages, snapshot, warning);
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }
}
