using System.IO;
using System.Windows;
using System.Windows.Media;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: ServiceBusEmulatorExplorer.ReadmeScreenshot <output-png>");
            return 2;
        }

        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        application.Startup += async (_, _) =>
        {
            InvestigationWorkspace? workspace = null;
            try
            {
                workspace = await RetailScreenshotScenario.CreateWorkspaceAsync();
                var window = new InvestigationWindow(workspace);

                // Give the production window its intended viewport before its normal
                // Loaded handler chooses the responsive layout. This keeps capture
                // independent of the hosted runner's small virtual desktop.
                window.Measure(new Size(
                    workspace.Preferences.WindowWidth,
                    workspace.Preferences.WindowHeight));
                window.Arrange(new Rect(
                    0,
                    0,
                    workspace.Preferences.WindowWidth,
                    workspace.Preferences.WindowHeight));
                window.UpdateLayout();
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                await window.Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.UpdateLayout();
                RetailScreenshotScenario.ValidateRenderedWindow(window, workspace);
                WpfScreenshot.SaveWindowContent(window, Path.GetFullPath(args[0]));
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                if (workspace is not null)
                {
                    await workspace.DisposeAsync();
                }
                application.Shutdown(exitCode);
            }
        };

        application.Run();
        return exitCode;
    }
}
