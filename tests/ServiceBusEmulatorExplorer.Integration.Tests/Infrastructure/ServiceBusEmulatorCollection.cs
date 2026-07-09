namespace ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ServiceBusEmulatorCollection : ICollectionFixture<ServiceBusEmulatorFixture>
{
    public const string Name = "Service Bus emulator integration";
}

public sealed class ServiceBusEmulatorFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(timeout.Token);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
