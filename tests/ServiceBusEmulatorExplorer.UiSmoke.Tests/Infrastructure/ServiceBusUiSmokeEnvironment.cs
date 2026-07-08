namespace ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

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

    private static string? GetRequiredVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static bool HasVariable(string name)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
    }
}
