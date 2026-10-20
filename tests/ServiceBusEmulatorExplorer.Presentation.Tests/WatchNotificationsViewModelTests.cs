using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WatchNotificationsViewModelTests
{
    [Fact]
    public async Task Requires_a_workspace_and_a_host()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        Assert.Equal("workspace", Assert.Throws<ArgumentNullException>(() => new WatchNotificationsViewModel(null!, new Host(fixture))).ParamName);
        Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => new WatchNotificationsViewModel(fixture.Workspace, null!)).ParamName);
    }

    [Fact]
    public async Task The_notification_shows_the_latest_arrival_of_the_first_location_and_counts_the_others()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, new Host(fixture));
        Assert.Null(notifications.Current);

        Arrive(fixture, fixture.Rows[0].Delivery, fixture.Rows[1].Delivery, Elsewhere(fixture.Rows[2].Delivery));

        var current = notifications.Current!;
        Assert.Equal(fixture.Rows[1].Delivery, current.Latest.Delivery);
        Assert.Equal(2, current.Count);
        Assert.Equal(1, current.OtherLocations);
        Assert.Equal(fixture.Workspace.SelectedProfile.Connection.Name, current.ConnectionName);
        Assert.Equal(fixture.Workspace.SelectedProfile.ColorHex, current.ColorHex);

        await fixture.Workspace.ApplyPreferencesAsync(fixture.Workspace.Preferences with { NotificationsEnabled = false });
        Assert.Null(notifications.Current);
    }

    [Fact]
    public async Task Dismissing_acknowledges_only_the_shown_location()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, new Host(fixture));
        var elsewhere = Elsewhere(fixture.Rows[2].Delivery);
        Arrive(fixture, fixture.Rows[0].Delivery, elsewhere);
        var acknowledging = new List<bool>();
        fixture.Workspace.Watch.PendingArrivals.CollectionChanged += (_, _) => acknowledging.Add(notifications.IsAcknowledging);
        var announced = new List<string?>();
        notifications.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        notifications.Dismiss();

        Assert.Equal([elsewhere], fixture.Workspace.Watch.PendingArrivals);
        Assert.Equal([true], acknowledging);
        Assert.False(notifications.IsAcknowledging);
        Assert.Equal([nameof(notifications.Current)], announced);
        Assert.Equal(0, notifications.Current!.OtherLocations);
    }

    [Fact]
    public async Task Investigating_while_disconnected_keeps_the_notification_and_says_why()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host(fixture);
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, host);
        var delivery = fixture.Rows[0].Delivery;
        await fixture.Workspace.DisconnectAsync();
        Arrive(fixture, delivery);

        await notifications.InvestigateAsync();

        Assert.Equal(1, host.Restored);
        Assert.Empty(host.Searches);
        Assert.Single(fixture.Workspace.Watch.PendingArrivals);
        Assert.Contains("Reconnect to investigate the watched messages. Notification retained.", fixture.Log);
    }

    [Fact]
    public async Task Investigating_searches_for_the_cases_focuses_the_latest_and_acknowledges_what_it_found()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host(fixture);
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, host);
        var latest = fixture.Rows[1].Delivery;
        Arrive(fixture, fixture.Rows[0].Delivery, latest);
        var investigating = new List<bool>();
        notifications.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(notifications.IsInvestigating)) investigating.Add(notifications.IsInvestigating); };

        await notifications.InvestigateAsync();

        var search = Assert.Single(host.Searches);
        Assert.Equal(WatchInvestigationQuery.Build([fixture.Rows[0].Delivery, latest], "", false, false), search);
        Assert.Equal(1, host.Restored);
        Assert.Equal(latest.Message.MessageId, fixture.Workspace.Search.FocusedMessage!.MessageId);
        Assert.Same(fixture.Workspace.Search.FocusedMessage, Assert.Single(host.Revealed));
        Assert.Empty(fixture.Workspace.Watch.PendingArrivals);
        Assert.Equal([true, false], investigating);
    }

    [Fact]
    public async Task Cases_the_search_did_not_find_stay_notified()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, new Host(fixture));
        var found = fixture.Rows[0].Delivery;
        var missing = Renamed(fixture.Rows[1].Delivery, "never-peeked");
        Arrive(fixture, found, missing);

        await notifications.InvestigateAsync();

        Assert.Equal([missing], fixture.Workspace.Watch.PendingArrivals);
        Assert.Contains("Some watched cases were not found in the scanned results. Their notification is retained; continue the search or dismiss it.", fixture.Log);
    }

    [Fact]
    public async Task A_search_replaced_while_investigating_acknowledges_nothing()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host(fixture);
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, host);
        Arrive(fixture, fixture.Rows[0].Delivery);
        var held = fixture.Gate();
        fixture.Peeks.BeforePeek = () => held.Task;

        Task investigating = notifications.InvestigateAsync();
        Task replacing = fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        fixture.Peeks.BeforePeek = () => Task.CompletedTask;
        held.SetResult();
        await Task.WhenAll(investigating, replacing).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(fixture.Workspace.Watch.PendingArrivals);
        Assert.Empty(host.Revealed);
        Assert.False(notifications.IsInvestigating);
    }

    [Fact]
    public async Task A_connection_changed_while_investigating_acknowledges_nothing()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var workspace = fixture.Workspace;
        var host = new Host(fixture)
        {
            Search = async (query, defaultMessageId) =>
            {
                await Task.Yield();
                await workspace.Search.StartAsync(query, defaultMessageId);
                await workspace.DisconnectAsync();
                await workspace.ConnectAsync();
            }
        };
        var notifications = new WatchNotificationsViewModel(workspace, host);
        Arrive(fixture, fixture.Rows[0].Delivery);
        var pending = workspace.Watch.PendingArrivals[0];

        await notifications.InvestigateAsync();

        Assert.Contains(pending, workspace.Watch.PendingArrivals);
        Assert.Empty(host.Revealed);
    }

    [Fact]
    public async Task A_failed_investigation_keeps_the_notification_and_a_second_one_waits_for_the_first()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var gate = fixture.Gate();
        var host = new Host(fixture) { Search = (_, _) => gate.Task.ContinueWith(_ => throw new InvalidOperationException("search failed")) };
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, host);
        Arrive(fixture, fixture.Rows[0].Delivery);

        Task first = notifications.InvestigateAsync();
        Assert.True(notifications.IsInvestigating);
        Assert.True(notifications.InvestigateAsync().IsCompletedSuccessfully);
        gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, host.Restored);
        Assert.Single(fixture.Workspace.Watch.PendingArrivals);
        Assert.Contains(fixture.Workspace.Activity, entry => entry.Warning && entry.Message == "Could not investigate watched messages. Notification retained.");
        Assert.False(notifications.IsInvestigating);
    }

    [Fact]
    public async Task A_case_with_a_correlation_id_is_found_by_it_even_under_another_message_id()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host(fixture);
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, host);
        var original = fixture.Rows[0].Delivery;
        var copy = new MessageDelivery(original.Identity with { SequenceNumber = 98 },
            original.Message with { MessageId = "replayed-copy", SequenceNumber = 98 });
        Arrive(fixture, copy);

        await notifications.InvestigateAsync();

        Assert.Empty(fixture.Workspace.Watch.PendingArrivals);
        Assert.Equal(original.Message.MessageId, Assert.Single(host.Revealed).MessageId);
    }

    [Fact]
    public async Task Investigating_during_a_valid_search_keeps_that_search_in_the_query()
    {
        await using var fixture = await WorkspaceFixture.OpenAsync();
        var host = new Host(fixture);
        var notifications = new WatchNotificationsViewModel(fixture.Workspace, host);
        await fixture.Workspace.Search.StartAsync("checkout*", defaultMessageId: true);
        var pending = fixture.Workspace.Search.Messages[0].Delivery;
        Arrive(fixture, pending);

        await notifications.InvestigateAsync();

        Assert.Equal(WatchInvestigationQuery.Build([pending], "checkout*", true, true), Assert.Single(host.Searches));
    }

    private static void Arrive(WorkspaceFixture fixture, params MessageDelivery[] deliveries)
    {
        foreach (var delivery in deliveries) fixture.Workspace.Watch.PendingArrivals.Add(delivery);
    }

    private static MessageDelivery Elsewhere(MessageDelivery delivery) =>
        new(delivery.Identity with { Source = new EntityAddress(EntityKind.Subscription, "billing", "order-events") }, delivery.Message);

    private static MessageDelivery Renamed(MessageDelivery delivery, string messageId) =>
        new(delivery.Identity with { SequenceNumber = 99 }, delivery.Message with { MessageId = messageId, SequenceNumber = 99, CorrelationId = null });

    private sealed class Host(WorkspaceFixture fixture) : IWatchNotificationHost
    {
        public int Restored { get; private set; }
        public List<WatchSearchRequest> Searches { get; } = [];
        public List<MessageRow> Revealed { get; } = [];
        public Func<string, bool, Task>? Search { get; init; }

        public void Restore() => Restored++;

        public async Task SearchAsync(string query, bool defaultMessageId)
        {
            Searches.Add(new(query, defaultMessageId));
            if (Search is not null) { await Search(query, defaultMessageId); return; }
            await fixture.Workspace.Search.StartAsync(query, defaultMessageId);
        }

        public void Reveal(MessageRow row) => Revealed.Add(row);
    }
}
