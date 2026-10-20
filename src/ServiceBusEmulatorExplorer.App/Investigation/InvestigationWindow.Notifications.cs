using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private WatchNotificationWindow? watchNotification;
    private bool closingNotification;

    private void WatchArrivalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!watchNotifications.IsAcknowledging) ShowWatchNotification();
    }

    private void WatchNotificationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WatchNotificationsViewModel.Current) or nameof(WatchNotificationsViewModel.IsInvestigating))
            ShowWatchNotification();
    }

    private void ShowWatchNotification()
    {
        if (!ready) return;
        if (closing || closePending || watchNotifications.Current is not { } notification)
        {
            CloseWatchNotification();
            return;
        }
        if (watchNotification is null)
        {
            watchNotification = new WatchNotificationWindow(InvestigateWatchNotification, watchNotifications.Dismiss, this);
            watchNotification.Closed += (_, _) =>
            {
                watchNotification = null;
                if (!closingNotification) watchNotifications.Dismiss();
            };
        }
        ProfileTheme.Apply(watchNotification, notification.ColorHex);
        watchNotification.Update(notification.Latest, notification.Count, notification.OtherLocations, notification.ConnectionName);
        watchNotification.IsEnabled = !watchNotifications.IsInvestigating;
        if (!watchNotification.IsVisible) watchNotification.Show();
    }

    private void CloseWatchNotification()
    {
        closingNotification = true;
        try { watchNotification?.Close(); watchNotification = null; }
        finally { closingNotification = false; }
    }

    private async void InvestigateWatchNotification() => await watchNotifications.InvestigateAsync();

    void IWatchNotificationHost.Restore() => RestoreFromTray();

    Task IWatchNotificationHost.SearchAsync(string query, bool defaultMessageId)
    {
        SearchBox.Text = query;
        return BeginGlobalSearchAsync(defaultMessageId);
    }

    void IWatchNotificationHost.Reveal(MessageRow row) => MessageGrid.ScrollIntoView(row);
}
