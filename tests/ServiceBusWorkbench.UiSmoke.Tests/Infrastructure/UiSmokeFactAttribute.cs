namespace ServiceBusWorkbench.UiSmoke.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiSmokeFactAttribute : FactAttribute
{
    private const int TimeoutMilliseconds = 60_000;

    public UiSmokeFactAttribute()
    {
        Timeout = TimeoutMilliseconds;

        if (!IsEnabled())
        {
            Skip = "Set SBW_RUN_UI_TESTS=true to run WPF FlaUI/UIA3 smoke tests.";
        }
    }

    private static bool IsEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SBW_RUN_UI_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
