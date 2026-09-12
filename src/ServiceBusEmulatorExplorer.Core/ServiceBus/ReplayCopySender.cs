using Azure.Identity;
using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public interface IReplayCopySender : IAsyncDisposable
{
    Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken);
}

/// <summary>A dedicated client prevents automatic SDK retries from duplicating an uncertain replay.</summary>
public sealed class ReplayCopySender : IReplayCopySender
{
    private readonly ServiceBusClient client;

    public ReplayCopySender(ConnectionProfile profile)
    {
        var options = new ServiceBusClientOptions { RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(30) } };
        client = profile.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
            ? new ServiceBusClient(profile.FullyQualifiedNamespace, new AzureCliCredential(), options)
            : new ServiceBusClient(profile.RuntimeConnectionString, options);
    }

    public async Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken)
    {
        if (destination.Kind is not (EntityKind.Queue or EntityKind.Topic) || string.IsNullOrWhiteSpace(destination.Name))
            throw new ArgumentException("Replay destination must be a queue or topic.", nameof(destination));
        var sender = client.CreateSender(destination.Name);
        try { await sender.SendMessageAsync(message, cancellationToken); }
        finally
        {
            // Sender cleanup must not turn a confirmed send into an apparent send failure.
            try { await sender.DisposeAsync(); }
            catch (Exception) { }
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
