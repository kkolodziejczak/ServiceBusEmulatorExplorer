using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var application = new Application();
        var window = new PrototypeWindow();
        if (args.Length == 2 && args[0] == "--verify")
        {
            string output = Path.GetFullPath(args[1]);
            window.Loaded += async (_, _) =>
            {
                int result = await PrototypeProof.RunAsync(window, output);
                application.Shutdown(result);
            };
        }
        else if (args.Length != 0)
        {
            return 2;
        }

        return application.Run(window);
    }
}
