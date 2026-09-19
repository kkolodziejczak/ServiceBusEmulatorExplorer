using System.IO;
using System.Windows;
using System.Windows.Interop;
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
                var window = new InvestigationWindow(workspace)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 0,
                    Top = 0
                };

                window.Show();
                await window.Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.Width = workspace.Preferences.WindowWidth;
                window.Height = workspace.Preferences.WindowHeight;
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
