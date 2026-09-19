using Azure.Identity;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

/// <summary>Dedicated DLQ-only receiver; settlement errors are not automatically retried.</summary>
public sealed class DlqDeleteReceiver : IDlqDeleteReceiver
{
    private readonly ServiceBusClient client;
    private readonly ServiceBusReceiver receiver;

    public DlqDeleteReceiver(ConnectionProfile profile, EntityAddress source)
    {
        _ = ReplayLineage.Destination(source);
        var options = new ServiceBusClientOptions { RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(30) } };
        client = profile.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
            ? new ServiceBusClient(profile.FullyQualifiedNamespace, new AzureCliCredential(), options)
            : new ServiceBusClient(profile.RuntimeConnectionString, options);
        var receiverOptions = new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock, PrefetchCount = 0 };
        receiver = source.Kind == EntityKind.Subscription ? client.CreateReceiver(source.TopicName, source.Name, receiverOptions)
            : client.CreateReceiver(source.Name, receiverOptions);
    }

    public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken) =>
        receiver.ReceiveMessagesAsync(take, TimeSpan.FromSeconds(5), cancellationToken);

    public Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) =>
        receiver.CompleteMessageAsync(message, cancellationToken);

    public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) =>
        receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try { await receiver.DisposeAsync().ConfigureAwait(false); }
        finally { await client.DisposeAsync().ConfigureAwait(false); }
    }
}
