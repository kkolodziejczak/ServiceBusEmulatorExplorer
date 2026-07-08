namespace ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiSmokeFactAttribute : FactAttribute
{
    public UiSmokeFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = "Set SBE_RUN_UI_TESTS=true to run WPF FlaUI/UIA3 smoke tests.";
        }
    }

    private static bool IsEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SBE_RUN_UI_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
