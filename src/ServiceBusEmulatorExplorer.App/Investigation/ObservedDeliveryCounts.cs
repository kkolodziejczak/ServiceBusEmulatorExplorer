using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

internal sealed record ObservedDeliveryCount(
    IReadOnlySet<DeliveryIdentity> Deliveries,
    bool IsComplete,
    DateTimeOffset CheckedAtUtc)
{
    public string Display => $"{Deliveries.Count:N0}*";
    public string Detail(MessageBucket bucket) =>
        $"Observed {Deliveries.Count:N0} {(bucket == MessageBucket.DeadLetter ? "dead-letter" : "main-queue")} deliveries; " +
        $"{(IsComplete ? "scan complete" : "partial scan")}; last checked {CheckedAtUtc:yyyy-MM-dd HH:mm:ss} UTC. " +
        "This is a browsing observation, not a live broker total. " +
        (bucket == MessageBucket.Active ? "Main-queue peeks can include scheduled, deferred and expired messages. " : "") +
        "Topic observations count deliveries across subscriptions.";

    public ObservedDeliveryCount Without(IReadOnlySet<DeliveryIdentity> deleted) =>
        this with { Deliveries = Deliveries.Where(identity => !deleted.Contains(identity)).ToHashSet() };
}

/// <summary>Observations are kept on session-owned tree nodes, never persisted or inferred from broker zeroes.</summary>
internal static class ObservedDeliveryCounts
{
    public static void Capture(
        IReadOnlyList<EntityNode> nodes, EntityNode selected, IEnumerable<MessageRow> rows,
        MessageBucket bucket, bool complete, DateTimeOffset checkedAtUtc)
    {
        var observed = rows.Where(row => row.ObservationDetail.Length == 0)
            .Select(row => row.Key).Where(key => key.Bucket == bucket).ToHashSet();
        if (selected.Kind == nameof(EntityKind.Topic))
        {
            foreach (EntityNode child in selected.Children)
            {
                var deliveries = observed.Where(key => key.Source == child.Address).ToHashSet();
                child.SetObservedCount(bucket, deliveries.Count > 0 || complete
                    ? new(deliveries, complete, checkedAtUtc) : null);
            }
        }
        else
        {
            selected.SetObservedCount(bucket, new(observed.Where(key => key.Source == selected.Address).ToHashSet(), complete, checkedAtUtc));
        }
        AggregateTopics(nodes);
        if (selected.Kind == nameof(EntityKind.Topic) && selected.Children.Count == 0)
            selected.SetObservedCount(bucket, new(observed, complete, checkedAtUtc));
    }

    public static void AggregateTopics(IEnumerable<EntityNode> nodes)
    {
        foreach (EntityNode topic in nodes.Where(node => node.Kind == nameof(EntityKind.Topic) && node.Children.Count > 0))
        {
            foreach (MessageBucket bucket in new[] { MessageBucket.Active, MessageBucket.DeadLetter })
            {
                var counts = topic.Children.Select(child => child.GetObservedCount(bucket)).ToArray();
                var known = counts.OfType<ObservedDeliveryCount>().ToArray();
                topic.SetObservedCount(bucket, known.Length == 0 ? null : new(
                    known.SelectMany(count => count.Deliveries).ToHashSet(),
                    known.Length == counts.Length && known.All(count => count.IsComplete),
                    known.Min(count => count.CheckedAtUtc)));
            }
        }
    }

    public static void ForgetDeleted(IReadOnlyList<EntityNode> nodes, IReadOnlySet<DeliveryIdentity> deleted)
    {
        foreach (EntityNode node in nodes)
        {
            foreach (MessageBucket bucket in new[] { MessageBucket.Active, MessageBucket.DeadLetter })
                if (node.GetObservedCount(bucket) is { } count)
                    node.SetObservedCount(bucket, count.Without(deleted));
        }
        AggregateTopics(nodes);
    }
}
