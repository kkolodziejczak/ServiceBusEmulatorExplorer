using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record WorkbenchDestination(string Name, EntityKind Kind);
public enum WorkbenchDestinationStatus { Unset, Available, Missing, Unverified }

public static class WorkbenchDestinationAvailability
{
    public static WorkbenchDestinationStatus Evaluate(WorkbenchDestination? destination, EntityDiscoverySnapshot? snapshot)
    {
        if (destination is null || string.IsNullOrWhiteSpace(destination.Name)) return WorkbenchDestinationStatus.Unset;
        if (snapshot is null) return WorkbenchDestinationStatus.Unverified;
        if (snapshot.Entities.Any(item => item.Entity.Kind == destination.Kind &&
            item.Entity.Name.Equals(destination.Name, StringComparison.OrdinalIgnoreCase))) return WorkbenchDestinationStatus.Available;
        return snapshot.IsComplete ? WorkbenchDestinationStatus.Missing : WorkbenchDestinationStatus.Unverified;
    }
}
