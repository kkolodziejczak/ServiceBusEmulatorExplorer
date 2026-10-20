using System.ComponentModel;
using System.Windows;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private readonly Func<Action, Action, IDisposable> createTray;
    private IDisposable? tray;
    private bool exitRequested;
    private bool closePending;

    public void InitializeSystemTray()
    {
        if (tray is not null || closing || closePending) return;
        try
        {
            tray = createTray(RestoreFromTray, ExitFromTray);
            Closed += (_, _) => { tray?.Dispose(); tray = null; };
        }
        catch (Exception)
        {
            workspace.Log("System tray unavailable. Closing the window will exit the application.", true);
        }
    }

    private void RestoreFromTray()
    {
        if (closing || closePending) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        if (closing || closePending) return;
        RestoreFromTray();
        exitRequested = true;
        Close();
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (closePending) return;
        if (windowClose.HidesToTray(exitRequested, tray is not null))
        {
            Hide();
            return;
        }

        closePending = true;
        try
        {
            if (!await windowClose.ConfirmCloseAsync()) return;
            refreshTimer.Stop();
            var bounds = WindowState == WindowState.Normal ? new Rect(0, 0, ActualWidth, ActualHeight) : RestoreBounds;
            await windowClose.ShutDownAsync(bounds.Width, bounds.Height);
            workspace.PropertyChanged -= WorkspaceChanged;
            workspace.Surface.PropertyChanged -= SurfaceChanged;
            workspace.Browse.PropertyChanged -= WorkbenchDiscoveryChanged;
            workspace.Inspector.PropertyChanged -= InspectorChanged;
            workspace.Activity.CollectionChanged -= ActivityChanged;
            workspace.Watch.PendingArrivals.CollectionChanged -= WatchArrivalsChanged;
            CloseWatchNotification();
            closing = true;
            // Leave the current Closing event before asking WPF to close again.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        finally
        {
            closePending = false;
            exitRequested = false;
        }
    }
}
