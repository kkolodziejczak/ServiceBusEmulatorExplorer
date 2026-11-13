namespace ServiceBusWorkbench.UiSmoke.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiNavigationSmokeFactAttribute : FactAttribute
{
    private const int TimeoutMilliseconds = 60_000;

    public UiNavigationSmokeFactAttribute()
    {
        Timeout = TimeoutMilliseconds;

        if (!IsUiSmokeEnabled())
        {
            Skip = "Set SBW_RUN_UI_TESTS=true to run WPF FlaUI/UIA3 smoke tests.";
            return;
        }

        if (!ServiceBusUiSmokeEnvironment.HasNavigationEnvironment())
        {
            Skip = "Set SBW_ADMIN_CONNECTION_STRING and SBW_RUNTIME_CONNECTION_STRING or SBW_CONNECTION_STRING to run navigation UI smoke tests.";
        }
    }

    private static bool IsUiSmokeEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SBW_RUN_UI_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
