using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class SelectorScreenshotScenario
{
    public static int Run(string output, string mode)
    {
        output = Path.GetFullPath(output);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            Window? window = null;
            try
            {
                bool settings = mode.StartsWith("--selector-settings", StringComparison.Ordinal);
                window = settings ? new SettingsWindow(new WorkspacePreferences(), _ => Task.CompletedTask) : mode switch
                {
                    "--selector-association" => new MessageLibraryPrototypeDialog(PrototypeDialogMode.Destination, "Sample workspace", "order-events", 3),
                    _ => throw new ArgumentException($"Unknown selector capture: {mode}")
                };
                string? accent = mode.Split('-').Last() switch
                {
                    "purple" => "#7540BF", "teal" => "#007F80", "orange" => "#B85B00", "red" => "#C83B3B", _ => null
                };
                if (accent is not null) ProfileTheme.Apply(window, accent);
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.Width = 980;
                window.Height = 800;
                window.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                WpfScreenshot.SaveWindowContent(window, output, (int)content.ActualWidth, (int)content.ActualHeight);
                var selector = (ComboBox)window.FindName(settings ? "QueuePageSizeSelector" : "DestinationPicker");
                selector.IsDropDownOpen = true;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                var popup = (Popup)selector.Template.FindName("PART_Popup", selector);
                var surface = (FrameworkElement)popup.Child;
                surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string popupPath = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-popup.png");
                using (var file = File.Create(popupPath)) encoder.Save(file);
                Console.WriteLine($"Captured open selector at {popupPath}");
                selector.IsDropDownOpen = false;
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { window?.Close(); app.Shutdown(result); }
        };
        app.Run();
        return result;
    }
}
