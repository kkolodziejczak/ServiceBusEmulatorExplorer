using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServiceBusEmulatorExplorer.App.Agent;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Connection;
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
                bool connections = mode == "--selector-settings-connections";
                bool agents = mode == "--selector-settings-agents";
                window = settings ? new SettingsWindow(connections ? ProductionProfilePreferences() : agents ? AgentAccessPreferences() : new WorkspacePreferences(),
                    _ => Task.CompletedTask, trayAvailable: true, agents ? new ListeningAgentStatus() : null) : mode switch
                {
                    "--selector-template-destination" => new Window { Content = new MessageLibraryPrototypeView() },
                    _ => throw new ArgumentException($"Unknown selector capture: {mode}")
                };
                string? accent = mode.Split('-').Last() switch
                {
                    "purple" => "#7540BF", "teal" => "#007F80", "orange" => "#B85B00", "red" => "#C83B3B", _ => null
                };
                if (connections) accent = "#C83B3B";
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
                var prototype = window.Content as MessageLibraryPrototypeView;
                if (prototype is not null)
                    ((Button)prototype.FindName("EditorPropertiesTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                if (connections || agents)
                {
                    // Connections: the selected profile's color, warning and agent switch. Agents: the listening state.
                    var tab = (TabItem)window.FindName(connections ? "ConnectionsTab" : "AgentsTab")!;
                    tab.IsSelected = true;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    if (connections) ((ScrollViewer)tab.Content).ScrollToEnd();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    window.UpdateLayout();
                    WpfScreenshot.SaveWindowContent(window, output, (int)content.ActualWidth, (int)content.ActualHeight);
                    result = 0;
                    return;
                }
                WpfScreenshot.SaveWindowContent(window, output, (int)content.ActualWidth, (int)content.ActualHeight);
                var selector = settings ? (ComboBox)window.FindName("QueuePageSizeSelector")! : (ComboBox)prototype!.FindName("TemplateDestination")!;
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

    private static WorkspacePreferences AgentAccessPreferences() => new()
    {
        AgentAccessEnabled = true,
        AgentAccessPort = AgentAccessDefaults.Port,
        AgentAccessToken = "screenshot-token"
    };

    private sealed class ListeningAgentStatus : IAgentAccessStatusSource
    {
        public AgentAccessStatus Status { get; } =
            new(AgentAccessState.Listening, AgentAccessDefaults.Port, LastRequestUtc: DateTimeOffset.UtcNow.AddMinutes(-2));
        public event Action<AgentAccessStatus>? StatusChanged { add { } remove { } }
    }

    private static WorkspacePreferences ProductionProfilePreferences() => new()
    {
        Profiles =
        [
            new("local-emulator", ConnectionProfileDefaults.LocalEmulator),
            new("production", new ConnectionProfile("Production",
                "Endpoint=sb://contoso-prod.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=example",
                "Endpoint=sb://contoso-prod.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=example"),
                "#C83B3B", "You are connecting to the live production namespace. Messages contain customer data.", AllowAgentAccess: false)
        ],
        SelectedProfileId = "production"
    };
}
