using System.Collections.Specialized;
using System.Windows;
using ServiceBusEmulatorExplorer.App.Investigation.Resources;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class InvestigationWindow
{
    private WatchNotificationWindow? watchNotification;
    private bool updatingNotifications;
    private bool closingNotification;
    private bool investigatingNotification;

    private void WatchArrivalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!updatingNotifications) ShowWatchNotification();
    }

    private MessageDelivery[][] PendingWatchGroups() => workspace.Watch.PendingArrivals
        .GroupBy(delivery => new WatchTarget(delivery.Identity.Source, delivery.Identity.Bucket))
        .Select(group => group.ToArray()).ToArray();

    private void ShowWatchNotification()
    {
        if (!ready) return;
        var groups = PendingWatchGroups();
        if (closing || closePending || !workspace.Preferences.NotificationsEnabled || groups.Length == 0)
        {
            CloseWatchNotification();
            return;
        }
        if (watchNotification is null)
        {
            watchNotification = new WatchNotificationWindow(InvestigateWatchNotification, DismissWatchNotification, this);
            watchNotification.Closed += (_, _) =>
            {
                watchNotification = null;
                if (!closingNotification) DismissWatchNotification();
            };
        }
        ProfileTheme.Apply(watchNotification, workspace.SelectedProfile.ColorHex);
        watchNotification.Update(new MessageRow(groups[0][^1], workspace.Preferences.TimestampDisplay),
            groups[0].Length, groups.Length - 1, workspace.SelectedProfile.Connection.Name);
        watchNotification.IsEnabled = !investigatingNotification;
        if (!watchNotification.IsVisible) watchNotification.Show();
    }

    private void CloseWatchNotification()
    {
        closingNotification = true;
        try { watchNotification?.Close(); watchNotification = null; }
        finally { closingNotification = false; }
    }

    private void DismissWatchNotification()
    {
        var group = PendingWatchGroups().FirstOrDefault();
        if (group is not null) AcknowledgeWatchArrivals(group);
    }

    private void AcknowledgeWatchArrivals(IEnumerable<MessageDelivery> deliveries)
    {
        updatingNotifications = true;
        try
        {
            foreach (var delivery in deliveries.ToArray()) workspace.Watch.PendingArrivals.Remove(delivery);
        }
        finally { updatingNotifications = false; }
        ShowWatchNotification();
    }

    private async void InvestigateWatchNotification()
    {
        if (investigatingNotification) return;
        var pending = PendingWatchGroups().FirstOrDefault();
        if (pending is null) return;
        RestoreFromTray();
        long? connection = workspace.Watch.ConnectionGeneration;
        if (!workspace.IsConnected || connection is null)
        {
            workspace.Log("Reconnect to investigate the watched messages. Notification retained.");
            return;
        }
        investigatingNotification = true;
        ShowWatchNotification();
        try
        {
            var request = WatchInvestigationQuery.Build(pending, workspace.Search.QueryText,
                workspace.Search.DefaultMessageId, workspace.Search.IsActive && workspace.Search.QueryError.Length == 0);
            SearchBox.Text = request.Query;
            Task search = BeginGlobalSearchAsync(request.DefaultMessageId);
            long revision = workspace.Search.Revision;
            await search;
            if (workspace.Watch.ConnectionGeneration != connection || !workspace.Search.IsActive || revision != workspace.Search.Revision) return;
            var observed = pending.Where(delivery => workspace.Search.Messages.Any(row => SameWatchedCase(delivery, row.Delivery))).ToArray();
            MessageRow? focus = workspace.Search.Messages.FirstOrDefault(row => row.Key == pending[^1].Identity)
                ?? workspace.Search.Messages.FirstOrDefault(row => SameWatchedCase(pending[^1], row.Delivery));
            if (focus is not null)
            {
                workspace.Search.FocusedMessage = focus;
                MessageGrid.ScrollIntoView(focus);
            }
            if (observed.Length < pending.Length)
                workspace.Log("Some watched cases were not found in the scanned results. Their notification is retained; continue the search or dismiss it.");
            AcknowledgeWatchArrivals(observed);
        }
        catch (Exception)
        {
            workspace.Log("Could not investigate watched messages. Notification retained.", true);
        }
        finally { investigatingNotification = false; ShowWatchNotification(); }
    }

    private static bool SameWatchedCase(MessageDelivery watched, MessageDelivery candidate) =>
        string.IsNullOrWhiteSpace(watched.Message.CorrelationId)
            ? string.Equals(watched.Message.MessageId, candidate.Message.MessageId, StringComparison.Ordinal)
            : string.Equals(watched.Message.CorrelationId, candidate.Message.CorrelationId, StringComparison.Ordinal);
}
