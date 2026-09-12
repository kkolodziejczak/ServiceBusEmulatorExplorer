using System.Windows.Controls;
using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private bool synchronizingConnection;
    private string? connectionWarning;

    private void InitializeConnectionPresentation()
    {
        synchronizingConnection = true;
        try { ConnectionSelector.ItemsSource = connectionSettings.Profiles; }
        finally { synchronizingConnection = false; }
        RefreshConnectionPresentation();
    }

    private void RefreshConnectionPresentation()
    {
        if (ConnectionSelector is null || ConnectionHealthText is null || ConnectionHealthDot is null) return;
        var profile = connectionSettings.SelectedProfile;
        synchronizingConnection = true;
        try
        {
            ConnectionSelector.Items.Refresh();
            ConnectionSelector.SelectedItem = profile;
        }
        finally { synchronizingConnection = false; }
        ConnectionSelector.ToolTip = profile.Name;
        ConnectionName.Text = profile.Name;
        ConnectionName.ToolTip = profile.Name;
        ProfileTheme.Apply(this, profile.ColorHex);
        if (settingsWindow is not null) ProfileTheme.Apply(settingsWindow, profile.ColorHex);
        if (globalWatchWindow is not null) ProfileTheme.Apply(globalWatchWindow, profile.ColorHex);
        if (watchNotification is not null) ProfileTheme.Apply(watchNotification, profile.ColorHex);
        UpdateInspector();
        UpdateSearchSurface();
        UpdateConnectionHealth();
    }

    private void UpdateConnectionHealth()
    {
        if (ConnectionHealthDot is null || ConnectionHealthText is null) return;
        var connected = Workspace.IsConnected;
        var warning = connected && connectionWarning is not null;
        ConnectionHealthText.Text = !connected ? "Disconnected" : warning ? "Warning" : "Connected";
        ConnectionHealthDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            !connected ? "#C83B3B" : warning ? "#A56700" : "#17823B"));
        var detail = !connected ? "Disconnected. Connect to inspect messages." : warning ? connectionWarning : "Connected to sample data.";
        ConnectionHealthText.ToolTip = detail;
        ConnectionHealthDot.ToolTip = detail;
        System.Windows.Automation.AutomationProperties.SetName(ConnectionHealthDot, ConnectionHealthText.Text);
    }

    private void SaveProfilePreferences()
    {
        RefreshConnectionPresentation();
        SavePreferences();
    }

    private void ConnectionSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingConnection || !IsInitialized || ConnectionSelector.SelectedItem is not PrototypeConnectionProfile profile) return;
        UseConnection(profile);
        RefreshConnectionPresentation();
    }

    private void MarkConnectionWarning(string reason)
    {
        connectionWarning = reason;
        UpdateConnectionHealth();
    }

    private void ClearConnectionWarning()
    {
        connectionWarning = null;
        UpdateConnectionHealth();
    }
}
