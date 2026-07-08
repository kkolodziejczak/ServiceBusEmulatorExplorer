using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

public sealed class MainWindowSmokeTests
{
    [UiSmokeFact]
    [Trait("TestCategory", "UiSmoke")]
    public void App_launches_main_window_and_exposes_shell_commands()
    {
        string executablePath = WpfAppPath.Resolve();
        Assert.True(File.Exists(executablePath), $"Build the WPF app before running UI smoke tests. Missing: {executablePath}");

        using Application application = Application.Launch(executablePath);
        using var automation = new UIA3Automation();

        Window window = application.GetMainWindow(automation, TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("Main window did not appear within 10 seconds.");

        Assert.Equal("Service Bus Emulator Explorer", window.Title);
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("ConnectButton")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("DisconnectButton")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("RefreshButton")));
        Assert.DoesNotContain(
            application.GetAllTopLevelWindows(automation),
            topLevelWindow => topLevelWindow.Title.Contains("Exception", StringComparison.OrdinalIgnoreCase));

        window.Close();
    }
}
