using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public sealed record ReplayLocation(EntityAddress Source, MessageBucket Bucket, string State);
public sealed record ReplayObservation(DateTimeOffset CheckedAtUtc, bool IsComplete, int SourceCount,
    int ScannedDeliveries, IReadOnlyList<ReplayLocation> Locations, string? Limitation = null)
{
    public bool IsAbsent => IsComplete && Locations.Count == 0;

    public void Validate()
    {
        if (CheckedAtUtc == default || SourceCount < 0 || ScannedDeliveries < 0 || Locations is null
            || (IsComplete && Limitation is not null))
            throw new ArgumentException("Invalid saved replay observation.");
        foreach (var location in Locations)
        {
            if (location is null || !Enum.IsDefined(location.Bucket) || string.IsNullOrWhiteSpace(location.State))
                throw new ArgumentException("Invalid saved replay location.");
            _ = ReplayLineage.Destination(location.Source);
        }
    }
}
public sealed record ReplayObservationBatch(IReadOnlyDictionary<string, ReplayObservation> Observations);
public enum ReplayOriginalStatus { Retained, Deleted, Unavailable, Uncertain }
