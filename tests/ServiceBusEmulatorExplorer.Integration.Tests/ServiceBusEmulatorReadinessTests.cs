using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

[Collection(ServiceBusEmulatorCollection.Name)]
public sealed class ServiceBusEmulatorReadinessTests
{
    [IntegrationFact]
    [Trait("TestCategory", "Integration")]
    public async Task Fixture_waits_until_administration_endpoint_is_ready()
    {
        await ServiceBusEmulatorEnvironment.WaitUntilReadyAsync(CancellationToken.None);
    }
}
