using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>What the window does for Watch notifications: bring itself back, search, and show a found message.</summary>
public interface IWatchNotificationHost
{
    /// <summary>Shows the window again when it is minimised or in the tray.</summary>
    void Restore();

    /// <summary>Puts <paramref name="query"/> in the search box and runs the search.</summary>
    Task SearchAsync(string query, bool defaultMessageId);

    /// <summary>Scrolls the message list to <paramref name="row"/>.</summary>
    void Reveal(MessageRow row);
}

/// <summary>
/// Watch notifications: new arrivals grouped by watched location, the notification for the first group, dismissing
/// it, and investigating it with a search that finds the same cases.
/// </summary>
public sealed class WatchNotificationsViewModel : ObservableObject
{
    private readonly InvestigationWorkspace workspace;
    private readonly IWatchNotificationHost host;
    private bool isInvestigating;

    public WatchNotificationsViewModel(InvestigationWorkspace workspace, IWatchNotificationHost host)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(host);
        this.workspace = workspace;
        this.host = host;
    }

    /// <summary><c>true</c> while arrivals are being acknowledged; the arrival list changes are this view model's own.</summary>
    public bool IsAcknowledging { get; private set; }

    /// <summary><c>true</c> while a notification is being investigated; the notification cannot be used meanwhile.</summary>
    public bool IsInvestigating
    {
        get => isInvestigating;
        private set => SetProperty(ref isInvestigating, value);
    }

    /// <summary>The notification to show, or <c>null</c> when notifications are off or nothing new arrived.</summary>
    public WatchNotificationState? Current
    {
        get
        {
            var groups = PendingGroups();
            if (!workspace.Preferences.NotificationsEnabled || groups.Length == 0) return null;
            var first = groups[0];
            return new(new MessageRow(first[^1], workspace.Preferences.TimestampDisplay, workspace.Preferences.DateFormat),
                first.Length, groups.Length - 1, workspace.SelectedProfile.Connection.Name, workspace.SelectedProfile.ColorHex);
        }
    }

    /// <summary>Acknowledges the arrivals of the notification shown.</summary>
    public void Dismiss()
    {
        if (PendingGroups().FirstOrDefault() is { } group) Acknowledge(group);
    }

    /// <summary>
    /// Searches for the shown notification's cases and focuses the latest. Arrivals the search found are acknowledged;
    /// the rest stay notified. Nothing happens while disconnected except a message saying so.
    /// </summary>
    public async Task InvestigateAsync()
    {
        if (IsInvestigating) return;
        var pending = PendingGroups().FirstOrDefault();
        if (pending is null) return;
        host.Restore();
        long? connection = workspace.Watch.ConnectionGeneration;
        if (!workspace.IsConnected || connection is null)
        {
            workspace.Log("Reconnect to investigate the watched messages. Notification retained.");
            return;
        }
        IsInvestigating = true;
        try
        {
            var search = workspace.Search;
            var request = WatchInvestigationQuery.Build(pending, search.QueryText, search.DefaultMessageId,
                search.IsActive && search.QueryError.Length == 0);
            Task searching = host.SearchAsync(request.Query, request.DefaultMessageId);
            long revision = search.Revision;
            await searching;
            if (workspace.Watch.ConnectionGeneration != connection || !search.IsActive || revision != search.Revision) return;
            var observed = pending.Where(delivery => search.Messages.Any(row => SameCase(delivery, row.Delivery))).ToArray();
            MessageRow? focus = search.Messages.FirstOrDefault(row => row.Key == pending[^1].Identity)
                ?? search.Messages.FirstOrDefault(row => SameCase(pending[^1], row.Delivery));
            if (focus is not null)
            {
                search.FocusedMessage = focus;
                host.Reveal(focus);
            }
            if (observed.Length < pending.Length)
                workspace.Log("Some watched cases were not found in the scanned results. Their notification is retained; continue the search or dismiss it.");
            Acknowledge(observed);
        }
        catch (Exception)
        {
            workspace.Log("Could not investigate watched messages. Notification retained.", true);
        }
        finally { IsInvestigating = false; }
    }

    private MessageDelivery[][] PendingGroups() => workspace.Watch.PendingArrivals
        .GroupBy(delivery => new WatchTarget(delivery.Identity.Source, delivery.Identity.Bucket))
        .Select(group => group.ToArray()).ToArray();

    private void Acknowledge(IEnumerable<MessageDelivery> deliveries)
    {
        IsAcknowledging = true;
        try
        {
            foreach (var delivery in deliveries.ToArray()) workspace.Watch.PendingArrivals.Remove(delivery);
        }
        finally { IsAcknowledging = false; }
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>A watched message and a found one are the same case when their correlation IDs match, or else their message IDs.</summary>
    private static bool SameCase(MessageDelivery watched, MessageDelivery candidate) =>
        string.IsNullOrWhiteSpace(watched.Message.CorrelationId)
            ? string.Equals(watched.Message.MessageId, candidate.Message.MessageId, StringComparison.Ordinal)
            : string.Equals(watched.Message.CorrelationId, candidate.Message.CorrelationId, StringComparison.Ordinal);
}

/// <summary>
/// A Watch notification: the latest arrival of the first watched location, how many arrived there, how many other
/// locations have arrivals, and the connection's name and colour.
/// </summary>
public sealed record WatchNotificationState(MessageRow Latest, int Count, int OtherLocations, string ConnectionName, string ColorHex);
