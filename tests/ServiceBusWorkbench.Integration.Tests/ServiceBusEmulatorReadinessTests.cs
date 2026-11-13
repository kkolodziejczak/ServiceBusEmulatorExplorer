using ServiceBusWorkbench.Integration.Tests.Infrastructure;

namespace ServiceBusWorkbench.Integration.Tests;

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
