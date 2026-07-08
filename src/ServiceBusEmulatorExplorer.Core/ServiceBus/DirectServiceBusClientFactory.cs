using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public sealed class DirectServiceBusClientFactory : IServiceBusClientFactory
{
    private ServiceBusAdministrationClient? _administrationClient;
    private ServiceBusClient? _runtimeClient;

    public ServiceBusAdministrationClient AdministrationClient =>
        _administrationClient ?? throw new InvalidOperationException("Connect before using the administration client.");

    public ServiceBusClient RuntimeClient =>
        _runtimeClient ?? throw new InvalidOperationException("Connect before using the runtime client.");

    public async Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        ValidationResult validation = ConnectionProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors), nameof(profile));
        }

        var administrationClient = new ServiceBusAdministrationClient(profile.AdministrationConnectionString);
        var runtimeClient = new ServiceBusClient(profile.RuntimeConnectionString);

        try
        {
            await ValidateAdministrationConnectionAsync(administrationClient, cancellationToken);
        }
        catch
        {
            await runtimeClient.DisposeAsync();
            throw;
        }

        await DisposeAsync();
        _administrationClient = administrationClient;
        _runtimeClient = runtimeClient;
    }

    private static async Task ValidateAdministrationConnectionAsync(
        ServiceBusAdministrationClient administrationClient,
        CancellationToken cancellationToken)
    {
        await foreach (QueueProperties _ in administrationClient
            .GetQueuesAsync(cancellationToken)
            .WithCancellation(cancellationToken))
        {
            break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_runtimeClient is not null)
        {
            await _runtimeClient.DisposeAsync();
        }

        _runtimeClient = null;
        _administrationClient = null;
    }
}
