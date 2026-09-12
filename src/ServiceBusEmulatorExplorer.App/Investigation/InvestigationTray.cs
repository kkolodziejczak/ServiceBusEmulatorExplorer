using System.Windows;
using Forms = System.Windows.Forms;

namespace ServiceBusEmulatorExplorer.App.Investigation;

internal sealed class InvestigationTray : IDisposable
{
    private readonly System.Drawing.Icon image;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.NotifyIcon icon;
    private bool disposed;

    public InvestigationTray(Action open, Action exit)
    {
        using var stream = Application.GetResourceStream(new Uri(
            "/ServiceBusEmulatorExplorer.App;component/Assets/AppIcon.ico", UriKind.Relative))!.Stream;
        try
        {
            image = new System.Drawing.Icon(stream);
            menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Service Bus Explorer", null, (_, _) => open());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => exit());
            icon = new Forms.NotifyIcon();
            icon.Icon = image;
            icon.Text = "Service Bus Emulator Explorer";
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += (_, _) => open();
            icon.Visible = true;
        }
        catch
        {
            icon?.Dispose();
            menu?.Dispose();
            image?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        image.Dispose();
    }
}
