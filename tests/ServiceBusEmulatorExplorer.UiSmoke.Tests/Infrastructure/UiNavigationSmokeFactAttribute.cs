namespace ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiNavigationSmokeFactAttribute : FactAttribute
{
    private const int TimeoutMilliseconds = 60_000;

    public UiNavigationSmokeFactAttribute()
    {
        Timeout = TimeoutMilliseconds;

        if (!IsUiSmokeEnabled())
        {
            Skip = "Set SBE_RUN_UI_TESTS=true to run WPF FlaUI/UIA3 smoke tests.";
            return;
        }

        if (!ServiceBusUiSmokeEnvironment.HasNavigationEnvironment())
        {
            Skip = "Set SBE_ADMIN_CONNECTION_STRING and SBE_RUNTIME_CONNECTION_STRING or SBE_CONNECTION_STRING to run navigation UI smoke tests.";
        }
    }

    private static bool IsUiSmokeEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SBE_RUN_UI_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
