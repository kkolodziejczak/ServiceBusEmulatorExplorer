namespace ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

using Azure.Messaging.ServiceBus.Administration;

public static class ServiceBusUiSmokeEnvironment
{
    public static string RuntimeConnectionString =>
        GetRequiredVariable("SBE_RUNTIME_CONNECTION_STRING")
        ?? GetRequiredVariable("SBE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("SBE_RUNTIME_CONNECTION_STRING or SBE_CONNECTION_STRING must be set.");

    public static string AdminConnectionString =>
        GetRequiredVariable("SBE_ADMIN_CONNECTION_STRING")
        ?? throw new InvalidOperationException("SBE_ADMIN_CONNECTION_STRING must be set.");

    public static bool HasNavigationEnvironment()
    {
        return HasVariable("SBE_ADMIN_CONNECTION_STRING")
            && (HasVariable("SBE_RUNTIME_CONNECTION_STRING") || HasVariable("SBE_CONNECTION_STRING"));
    }

    public static async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        var client = new ServiceBusAdministrationClient(AdminConnectionString);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));

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

    private static string? GetRequiredVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static bool HasVariable(string name)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
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
