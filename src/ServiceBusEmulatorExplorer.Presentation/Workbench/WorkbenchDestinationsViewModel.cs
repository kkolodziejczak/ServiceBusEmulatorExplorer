using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>
/// The Message Workbench's destinations: the queues and topics it may send to, either sample ones while offline or
/// the ones the connection discovered, whether a template's destination is available, and what to tell the user when
/// it is not.
/// </summary>
public sealed class WorkbenchDestinationsViewModel
{
    private static readonly WorkbenchDestination[] SampleDestinations =
        [new("order-events", EntityKind.Topic), new("inventory-events", EntityKind.Topic), new("order-replies", EntityKind.Queue)];

    private bool usesSamples = true;
    private long generation = -1;
    private EntityDiscoverySnapshot? snapshot;

    /// <summary>The profile whose discovery is shown, or <c>null</c> for the sample destinations.</summary>
    public string? ProfileId { get; private set; }

    /// <summary>The host of the connected namespace, shown on the review.</summary>
    public string Endpoint { get; private set; } = "localhost";

    /// <summary>
    /// Shows what a connection discovered. Discovery from an older connection is ignored, and so is the same
    /// discovery again; both return <c>null</c>.
    /// </summary>
    /// <param name="current">The destination of the template being edited, to tell whether its status changed.</param>
    public DestinationsChange? ShowDiscovery(string profileId, long discoveryGeneration, EntityDiscoverySnapshot? discovered,
        string endpoint, WorkbenchDestination current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (discoveryGeneration < generation) return null;
        if (!usesSamples && ProfileId == profileId && discoveryGeneration == generation &&
            ReferenceEquals(snapshot, discovered) && Endpoint == endpoint) return null;
        var previous = StatusOf(current);
        bool profileChanged = ProfileId != profileId || generation != discoveryGeneration;
        usesSamples = false;
        ProfileId = profileId;
        generation = discoveryGeneration;
        snapshot = discovered;
        Endpoint = endpoint;
        return new(profileChanged, previous != StatusOf(current));
    }

    /// <summary>Goes back to the sample destinations used offline; returns <c>null</c> when they are already shown.</summary>
    public DestinationsChange? UseSamples(WorkbenchDestination current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (usesSamples) return null;
        var previous = StatusOf(current);
        usesSamples = true;
        ProfileId = null;
        generation = -1;
        snapshot = null;
        Endpoint = "localhost";
        return new(false, previous != StatusOf(current));
    }

    public WorkbenchDestinationStatus StatusOf(WorkbenchDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Name.Length == 0) return WorkbenchDestinationStatus.Unset;
        if (usesSamples) return SampleDestinations.Any(item => item.Kind == destination.Kind && item.Name == destination.Name)
            ? WorkbenchDestinationStatus.Available : WorkbenchDestinationStatus.Unverified;
        return WorkbenchDestinationAvailability.Evaluate(destination, snapshot);
    }

    /// <summary>
    /// The destinations to choose from, by name. The current one is listed first when it was not discovered, so the
    /// choice still shows it.
    /// </summary>
    public IReadOnlyList<WorkbenchDestination> ChoicesFor(WorkbenchDestination current)
    {
        ArgumentNullException.ThrowIfNull(current);
        IEnumerable<WorkbenchDestination> discovered = usesSamples ? SampleDestinations
            : snapshot?.Entities.Where(item => item.Entity.Kind is EntityKind.Topic or EntityKind.Queue)
                .Select(item => new WorkbenchDestination(item.Entity.Name, item.Entity.Kind)) ?? [];
        var choices = discovered.Distinct().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (current.Name.Length > 0 && !choices.Any(item => item.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase) && item.Kind == current.Kind))
            choices.Insert(0, current);
        return choices;
    }

    /// <summary>The notice in Compose and Prepare when the template's destination cannot be sent to.</summary>
    public static string NoticeFor(WorkbenchDestination current, WorkbenchDestinationStatus status) => status switch
    {
        WorkbenchDestinationStatus.Missing => $"{current.Kind} \"{current.Name}\" is unavailable.\nChoose an available destination to continue.",
        WorkbenchDestinationStatus.Unverified => $"{current.Kind} \"{current.Name}\" is not verified.\nCheck the connection and reconnect to refresh entity discovery.",
        WorkbenchDestinationStatus.Unset => "No destination selected.\nChoose a queue or topic to continue.",
        _ => ""
    };

    /// <summary>The warning beside a template in the library when its destination cannot be sent to.</summary>
    public static string LibraryWarningFor(string name, WorkbenchDestinationStatus status) => status switch
    {
        WorkbenchDestinationStatus.Missing => $"Destination unavailable: {name}. Select an available queue or topic.",
        WorkbenchDestinationStatus.Unverified => $"Destination not verified: {name}. Connect and refresh discovery before sending.",
        _ => ""
    };

    /// <summary>The notice's action: checking the connection for an unverified destination, otherwise choosing one.</summary>
    public static string ActionFor(WorkbenchDestinationStatus status) =>
        status == WorkbenchDestinationStatus.Unverified ? "Check connection" : "Choose destination";
}

/// <summary>What a discovery update changed: the profile or connection, and the template's destination status.</summary>
public sealed record DestinationsChange(bool ProfileChanged, bool CurrentStatusChanged);
