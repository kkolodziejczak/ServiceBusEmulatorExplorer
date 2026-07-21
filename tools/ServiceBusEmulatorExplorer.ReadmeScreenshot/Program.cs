using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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

        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var viewModel = RetailScreenshotScenario.CreateViewModelAsync().GetAwaiter().GetResult();
            var window = new ServiceBusEmulatorExplorer.App.MainWindow(viewModel)
            {
                ShowActivated = false
            };

            try
            {
                WpfScreenshot.SaveWindowContent(window, Path.GetFullPath(args[0]));
            }
            finally
            {
                window.Close();
                application.Shutdown();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
