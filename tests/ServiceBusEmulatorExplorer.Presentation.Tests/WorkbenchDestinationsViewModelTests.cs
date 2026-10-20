using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchDestinationsViewModelTests
{
    private static readonly WorkbenchDestination OrderEvents = new("order-events", EntityKind.Topic);
    private static readonly WorkbenchDestination Orders = new("orders", EntityKind.Queue);

    [Fact]
    public void Offline_the_sample_destinations_are_available_and_others_unverified()
    {
        var destinations = new WorkbenchDestinationsViewModel();

        Assert.Null(destinations.ProfileId);
        Assert.Equal("localhost", destinations.Endpoint);
        Assert.Equal(WorkbenchDestinationStatus.Available, destinations.StatusOf(OrderEvents));
        Assert.Equal(WorkbenchDestinationStatus.Unverified, destinations.StatusOf(new("Order-Events", EntityKind.Topic)));
        Assert.Equal(WorkbenchDestinationStatus.Unverified, destinations.StatusOf(new("order-events", EntityKind.Queue)));
        Assert.Equal(WorkbenchDestinationStatus.Unset, destinations.StatusOf(new("", EntityKind.Topic)));
        Assert.Equal(["inventory-events", "order-events", "order-replies"], destinations.ChoicesFor(OrderEvents).Select(item => item.Name));
    }

    [Fact]
    public void Discovery_decides_availability_and_reports_what_changed()
    {
        var destinations = new WorkbenchDestinationsViewModel();
        var snapshot = Snapshot(complete: true, Orders);

        var first = destinations.ShowDiscovery("profile", 1, snapshot, "sb.local", OrderEvents);

        Assert.Equal(new DestinationsChange(true, true), first);
        Assert.Equal("profile", destinations.ProfileId);
        Assert.Equal("sb.local", destinations.Endpoint);
        Assert.Equal(WorkbenchDestinationStatus.Missing, destinations.StatusOf(OrderEvents));
        Assert.Equal(WorkbenchDestinationStatus.Available, destinations.StatusOf(new("ORDERS", EntityKind.Queue)));
        Assert.Null(destinations.ShowDiscovery("profile", 1, snapshot, "sb.local", OrderEvents));
        Assert.Null(destinations.ShowDiscovery("profile", 0, Snapshot(true), "sb.local", OrderEvents));

        var incomplete = Snapshot(complete: false, Orders);
        var refreshed = destinations.ShowDiscovery("profile", 1, incomplete, "sb.local", OrderEvents);

        Assert.Equal(new DestinationsChange(false, true), refreshed);
        Assert.Equal(WorkbenchDestinationStatus.Unverified, destinations.StatusOf(OrderEvents));
        Assert.Equal(new DestinationsChange(false, false), destinations.ShowDiscovery("profile", 1, incomplete, "other.local", OrderEvents));
        Assert.Equal("other.local", destinations.Endpoint);
        Assert.Equal(new DestinationsChange(true, false), destinations.ShowDiscovery("other", 1, incomplete, "other.local", OrderEvents));
        Assert.Equal(new DestinationsChange(true, false), destinations.ShowDiscovery("other", 2, incomplete, "other.local", OrderEvents));
    }

    [Fact]
    public void Without_discovery_every_destination_is_unverified()
    {
        var destinations = new WorkbenchDestinationsViewModel();

        destinations.ShowDiscovery("profile", 1, null, "", Orders);

        Assert.Equal(WorkbenchDestinationStatus.Unverified, destinations.StatusOf(Orders));
        Assert.Equal([Orders], destinations.ChoicesFor(Orders));
    }

    [Fact]
    public void Going_back_to_the_samples_resets_discovery_and_reports_a_status_change()
    {
        var destinations = new WorkbenchDestinationsViewModel();
        Assert.Null(destinations.UseSamples(OrderEvents));
        destinations.ShowDiscovery("profile", 3, Snapshot(true, Orders), "sb.local", OrderEvents);

        Assert.Equal(new DestinationsChange(false, true), destinations.UseSamples(OrderEvents));

        Assert.Null(destinations.ProfileId);
        Assert.Equal("localhost", destinations.Endpoint);
        Assert.Equal(WorkbenchDestinationStatus.Available, destinations.StatusOf(OrderEvents));
        Assert.Equal(new DestinationsChange(true, true), destinations.ShowDiscovery("profile", 0, Snapshot(true, Orders), "sb.local", OrderEvents));
    }

    [Fact]
    public void Choices_list_discovered_queues_and_topics_by_name_with_an_undiscovered_current_one_first()
    {
        var destinations = new WorkbenchDestinationsViewModel();
        var subscription = new WorkbenchDestination("billing", EntityKind.Subscription);
        destinations.ShowDiscovery("profile", 1, Snapshot(true, new("zeta", EntityKind.Topic), Orders, subscription, Orders), "", Orders);

        Assert.Equal([Orders, new("zeta", EntityKind.Topic)], destinations.ChoicesFor(Orders));
        Assert.Equal([Orders, new("zeta", EntityKind.Topic)], destinations.ChoicesFor(new("ORDERS", EntityKind.Queue)));
        Assert.Equal([OrderEvents, Orders, new("zeta", EntityKind.Topic)], destinations.ChoicesFor(OrderEvents));
        Assert.Equal([Orders, new("zeta", EntityKind.Topic)], destinations.ChoicesFor(new("", EntityKind.Topic)));
    }

    [Fact]
    public void Notices_and_warnings_say_what_is_wrong_and_what_to_do()
    {
        Assert.Equal("Topic \"order-events\" is unavailable.\nChoose an available destination to continue.",
            WorkbenchDestinationsViewModel.NoticeFor(OrderEvents, WorkbenchDestinationStatus.Missing));
        Assert.Equal("Queue \"orders\" is not verified.\nCheck the connection and reconnect to refresh entity discovery.",
            WorkbenchDestinationsViewModel.NoticeFor(Orders, WorkbenchDestinationStatus.Unverified));
        Assert.Equal("No destination selected.\nChoose a queue or topic to continue.",
            WorkbenchDestinationsViewModel.NoticeFor(new("", EntityKind.Topic), WorkbenchDestinationStatus.Unset));
        Assert.Equal("", WorkbenchDestinationsViewModel.NoticeFor(Orders, WorkbenchDestinationStatus.Available));

        Assert.Equal("Destination unavailable: orders. Select an available queue or topic.",
            WorkbenchDestinationsViewModel.LibraryWarningFor("orders", WorkbenchDestinationStatus.Missing));
        Assert.Equal("Destination not verified: orders. Connect and refresh discovery before sending.",
            WorkbenchDestinationsViewModel.LibraryWarningFor("orders", WorkbenchDestinationStatus.Unverified));
        Assert.Equal("", WorkbenchDestinationsViewModel.LibraryWarningFor("orders", WorkbenchDestinationStatus.Unset));

        Assert.Equal("Check connection", WorkbenchDestinationsViewModel.ActionFor(WorkbenchDestinationStatus.Unverified));
        Assert.Equal("Choose destination", WorkbenchDestinationsViewModel.ActionFor(WorkbenchDestinationStatus.Missing));
    }

    private static EntityDiscoverySnapshot Snapshot(bool complete, params WorkbenchDestination[] entities) => new(
        entities.Select(entity => new EntityObservation(
            new DiscoveredEntity(entity.Kind, entity.Name, entity.Kind == EntityKind.Subscription ? "order-events" : null,
                new EntityMetadata(entity.Name, "Active", null, null, null, null, null, null, null)),
            new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known), new(0, CountAvailability.Known))))
            .ToArray(),
        DateTimeOffset.UnixEpoch, complete, []);
}
