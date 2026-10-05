using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record DeleteSelectionState(IReadOnlyList<MessageDelivery> Targets, string? Problem)
{
    public bool CanDelete => Targets.Count > 0 && Problem is null;
}

public static class DeleteSelection
{
    public static DeleteSelectionState Evaluate(bool connected, IReadOnlyList<MessageRow> rows)
    {
        if (!connected) return new([], "Connect before deleting dead-letter messages.");
        var targets = rows.Where(row => row.IsSelected).ToArray();
        if (targets.Length == 0) return new([], "Check dead-letter messages to delete.");
        var deliveries = targets.Select(row => row.Delivery).DistinctBy(delivery => delivery.Identity).ToArray();
        return targets.Any(row => !row.IsDeadLetter)
            ? new(deliveries, "Select only dead-letter messages to delete.")
            : new(deliveries, null);
    }
}
