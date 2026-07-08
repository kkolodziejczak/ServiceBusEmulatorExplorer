using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

public static class ServiceBusEmulatorEnvironment
{
    public static async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        string connectionString = GetRequiredVariable("SBE_ADMIN_CONNECTION_STRING");
        var client = new ServiceBusAdministrationClient(connectionString);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (true)
        {
            try
            {
                await ProbeAdministrationClientAsync(client, timeout.Token);
                return;
            }
            catch when (!timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
        }
    }

    private static string GetRequiredVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{name} must be set for integration tests.");
    }

    private static async Task ProbeAdministrationClientAsync(
        ServiceBusAdministrationClient client,
        CancellationToken cancellationToken)
    {
        await foreach (QueueProperties _ in client.GetQueuesAsync(cancellationToken).WithCancellation(cancellationToken))
        {
            break;
        }
    }
}
