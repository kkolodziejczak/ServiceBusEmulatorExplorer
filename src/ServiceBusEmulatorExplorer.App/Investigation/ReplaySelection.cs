using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record ReplaySelectionState(IReadOnlyList<MessageDelivery> Targets, string? EditedBody, string? Problem)
{
    public bool CanReplay => Targets.Count > 0 && Problem is null;
}

public static class ReplaySelection
{
    public static ReplaySelectionState Evaluate(bool connected, IReadOnlyList<MessageRow> rows,
        MessageRow? focused, DeliveryInspector inspector)
    {
        if (!connected) return new([], null, null);
        var selected = rows.Where(row => row.IsSelected).ToArray();
        var targets = selected.Length > 0 ? selected : focused is null ? [] : new[] { focused };
        var deliveries = targets.Select(row => row.Delivery).ToArray();
        if (targets.Any(row => !row.IsDeadLetter))
            return new(deliveries, null, "Select only dead-letter messages to replay.");
        if (focused is not null && inspector.Current?.Identity == focused.Key && inspector.IsDirty)
        {
            if (targets.Length != 1 || targets[0].Key != focused.Key)
                return new(deliveries, null, "Select only this message to replay its edited JSON.");
            return inspector.IsValidJson ? new(deliveries, inspector.Document.Text, null)
                : new(deliveries, null, "Complete the JSON before replaying, or discard your changes.");
        }
        if (targets.Any(row => inspector.HasDraft(row.Key)))
            return new(deliveries, null, "A checked message has a draft. Open it to replay or discard its changes.");
        return new(deliveries, null, null);
    }
}
