using Avalonia;
using Avalonia.Headless;
using ServiceBusWorkbench.Avalonia;
using ServiceBusWorkbench.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ServiceBusWorkbench.Avalonia.Tests;

/// <summary>The real app on the headless platform, drawing through Skia so frames can be captured as pixels.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
