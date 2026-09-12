using System.ComponentModel;
using System.Windows;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private InvestigationTray? tray;
    private bool exitRequested;
    private bool closePending;

    public void InitializeSystemTray()
    {
        if (tray is not null || closing || closePending) return;
        try
        {
            tray = new InvestigationTray(RestoreFromTray, ExitFromTray);
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
        if (!exitRequested && tray is not null && workspace.Preferences.CloseToTray)
        {
            Hide();
            return;
        }

        closePending = true;
        try
        {
            if (workspace.Inspector.HasDrafts && !await workspace.ConfirmDiscard()) return;
            refreshTimer.Stop();
            var bounds = WindowState == WindowState.Normal ? new Rect(0, 0, ActualWidth, ActualHeight) : RestoreBounds;
            await workspace.UpdateWindowBoundsAsync(bounds.Width, bounds.Height);
            await workspace.DisposeAsync();
            workspace.PropertyChanged -= WorkspaceChanged;
            workspace.Surface.PropertyChanged -= SurfaceChanged;
            workspace.Inspector.PropertyChanged -= InspectorChanged;
            workspace.Activity.CollectionChanged -= ActivityChanged;
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
