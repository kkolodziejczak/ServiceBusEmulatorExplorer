using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public enum CountAvailability { Known, Unavailable, NotSupported, Stale }

public sealed record CountObservation(long? Value, CountAvailability Availability, string? Detail = null);

public sealed record EntityCountObservation(
    CountObservation Active,
    CountObservation DeadLetter,
    CountObservation Scheduled);

/// <summary>
/// The discovered identity and descriptive metadata for an entity.
/// Runtime counts are intentionally kept on <see cref="EntityObservation.Counts"/>
/// so unavailable values cannot be represented as legacy zeroes.
/// </summary>
public sealed record DiscoveredEntity(
    EntityKind Kind,
    string Name,
    string? TopicName,
    EntityMetadata Metadata);

public sealed record EntityObservation(DiscoveredEntity Entity, EntityCountObservation Counts)
{
    /// <summary>
    /// Compatibility constructor for existing callers that still create legacy
    /// service-bus nodes. The legacy numeric counts are discarded immediately.
    /// </summary>
    public EntityObservation(ServiceBusEntityNode entity, EntityCountObservation counts)
        : this(ToDiscoveredEntity(entity), counts)
    {
    }

    private static DiscoveredEntity ToDiscoveredEntity(ServiceBusEntityNode entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new DiscoveredEntity(entity.Kind, entity.Name, entity.TopicName, entity.Metadata);
    }
}

public sealed record EntityDiscoverySnapshot(
    IReadOnlyList<EntityObservation> Entities,
    DateTimeOffset ObservedAtUtc,
    bool IsComplete,
    IReadOnlyList<string> Issues);

public interface IInvestigationEntityBrowser
{
    Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken);
}
