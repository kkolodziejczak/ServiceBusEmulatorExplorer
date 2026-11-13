namespace ServiceBusWorkbench.Integration.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = "Set SBW_RUN_INTEGRATION_TESTS=true to run Docker-backed Service Bus emulator integration tests.";
        }
    }

    private static bool IsEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SBW_RUN_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
