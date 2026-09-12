using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record DeleteSelectionState(IReadOnlyList<MessageDelivery> Targets, string? Problem)
{
    public bool CanDelete => Targets.Count > 0 && Problem is null;
}

public static class DeleteSelection
{
    public static DeleteSelectionState Evaluate(bool connected, IReadOnlyList<MessageRow> rows, MessageRow? focused)
    {
        if (!connected) return new([], "Connect before deleting dead-letter messages.");
        var selected = rows.Where(row => row.IsSelected).ToArray();
        var targets = selected.Length > 0 ? selected
            : focused is not null && rows.Any(row => row.Key == focused.Key) ? new[] { focused } : [];
        var deliveries = targets.Select(row => row.Delivery).DistinctBy(delivery => delivery.Identity).ToArray();
        return targets.Any(row => !row.IsDeadLetter)
            ? new(deliveries, "Select only dead-letter messages to delete.")
            : new(deliveries, null);
    }
}
