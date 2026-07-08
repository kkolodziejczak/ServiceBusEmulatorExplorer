namespace ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = "Set SBE_RUN_INTEGRATION_TESTS=true to run Docker-backed Service Bus emulator integration tests.";
        }
    }

    private static bool IsEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SBE_RUN_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
