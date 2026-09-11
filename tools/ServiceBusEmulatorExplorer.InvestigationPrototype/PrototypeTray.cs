using System.IO;
using Forms = System.Windows.Forms;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal sealed class PrototypeTray : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon image;
    private readonly Forms.ContextMenuStrip menu;

    public PrototypeTray(Action show, Action exit)
    {
        image = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Service Bus Explorer", null, (_, _) => show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = image, Text = "Service Bus Emulator Explorer", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        icon.Visible = false; icon.Dispose(); menu.Dispose(); image.Dispose();
    }
}
