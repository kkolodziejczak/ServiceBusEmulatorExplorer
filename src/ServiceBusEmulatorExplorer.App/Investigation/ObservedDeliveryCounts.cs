using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

internal sealed record ObservedDeliveryCount(
    IReadOnlySet<DeliveryIdentity> Deliveries,
    DateTimeOffset CheckedAtUtc)
{
    // Exact below 1,000, then a bracket; the exact total stays in Exact and the tooltip.
    public string Display => Deliveries.Count switch
    {
        < 1_000 => $"{Deliveries.Count}*",
        < 10_000 => "1k+*",
        < 100_000 => "10k+*",
        _ => "100k+*"
    };
    public string Exact => $"{Deliveries.Count:N0}*";
    public string Detail(MessageBucket bucket, DateDisplayFormat format = DateDisplayFormat.Windows) =>
        $"Observed {Deliveries.Count:N0} {(bucket == MessageBucket.DeadLetter ? "dead-letter" : "main-queue")} deliveries\n" +
        $"Scanned: {DateDisplay.Timestamp(CheckedAtUtc, format)} UTC\n\n" +
        "This is a peek count, not a live broker total.\n" +
        (bucket == MessageBucket.Active ? "Main-queue peeks can include scheduled, deferred and expired messages.\n" : "") +
        "Topic observations count deliveries across subscriptions.";

    public ObservedDeliveryCount Without(IReadOnlySet<DeliveryIdentity> deleted) =>
        this with { Deliveries = Deliveries.Where(identity => !deleted.Contains(identity)).ToHashSet() };
}

/// <summary>Observations are kept on session-owned tree nodes, never persisted or inferred from broker zeroes.</summary>
internal static class ObservedDeliveryCounts
{
    /// <summary>
    /// A topic shows a new total only once every subscription has one. Until then it keeps its last
    /// total, minus deliveries of subscriptions that no longer exist; a subscription missing from an
    /// incomplete discovery clears it, since the last total can no longer be trusted.
    /// </summary>
    public static void AggregateTopics(IEnumerable<EntityNode> nodes)
    {
        foreach (EntityNode topic in nodes.Where(node => node.Kind == nameof(EntityKind.Topic) && node.Children.Count > 0))
        {
            bool staleChild = topic.Children.Any(child => child.Observation?.Counts.Active.Availability == CountAvailability.Stale);
            foreach (MessageBucket bucket in new[] { MessageBucket.Active, MessageBucket.DeadLetter })
            {
                var counts = topic.Children.Select(child => child.GetObservedCount(bucket)).ToArray();
                if (staleChild)
                    topic.SetObservedCount(bucket, null);
                else if (counts.All(count => count is not null))
                    topic.SetObservedCount(bucket, new(
                        counts.SelectMany(count => count!.Deliveries).ToHashSet(),
                        counts.Min(count => count!.CheckedAtUtc)));
                else if (topic.GetObservedCount(bucket) is { } last)
                {
                    var current = topic.Children.Select(child => child.Address).ToHashSet();
                    topic.SetObservedCount(bucket, last with { Deliveries = last.Deliveries.Where(delivery => current.Contains(delivery.Source)).ToHashSet() });
                }
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
