using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

// Emulator counts: an entity is counted in full when first opened, then recounted on every refresh.
public sealed partial class MessageBrowseWorkflow
{
    private static readonly MessageBucket[] CountedBuckets = [MessageBucket.Active, MessageBucket.DeadLetter];
    private ObservedCountScanner? counter;

    private void CountFirstTime(EntityNode node)
    {
        if (counter is null) return;
        if (node.Kind == nameof(EntityKind.Topic) && node.Children.Count == 0)
        {
            foreach (var countedBucket in CountedBuckets)
                node.SetObservedCount(countedBucket, node.GetObservedCount(countedBucket) ?? new(new HashSet<DeliveryIdentity>(), DateTimeOffset.UtcNow));
            return;
        }
        counter.Request(CountTargets(CountSources(node), uncountedOnly: true));
    }

    // Recounts everything already counted, the open entity (so a failed first count is retried)
    // and watched entities, which the user asked to follow without opening them.
    private void Recount()
    {
        if (counter is null) return;
        var queues = AllEntities().Where(node => node.Kind == nameof(EntityKind.Queue) && IsCounted(node));
        var subscriptions = AllEntities().Where(node => node.Kind == nameof(EntityKind.Topic))
            .SelectMany(topic => IsCounted(topic) ? topic.Children : topic.Children.Where(IsCounted));
        var open = selectedEntity is null ? [] : CountSources(selectedEntity);
        var watched = watchedScopes.Values.SelectMany(addresses => addresses).ToHashSet();
        var followed = AllEntities().Where(node => node.Address is { } address && watched.Contains(address));
        counter.Request(CountTargets(queues.Concat(subscriptions).Concat(open).Concat(followed).Distinct(), uncountedOnly: false));
    }

    private static EntityNode[] CountSources(EntityNode node) =>
        node.Kind == nameof(EntityKind.Topic) ? node.Children.ToArray() : [node];

    private static bool IsCounted(EntityNode node) => CountedBuckets.Any(countedBucket => node.GetObservedCount(countedBucket) is not null);

    private static WatchTarget[] CountTargets(IEnumerable<EntityNode> sources, bool uncountedOnly) => sources
        .SelectMany(source => CountedBuckets
            .Where(countedBucket => !uncountedOnly || source.GetObservedCount(countedBucket) is null)
            .Select(countedBucket => new WatchTarget(source.Address!, countedBucket)))
        .ToArray();

    private void PublishCount(WatchTarget target, ObservedDeliveryCount count)
    {
        if (AllEntities().FirstOrDefault(node => node.Address == target.Address) is not { } node) return;
        node.SetObservedCount(target.Bucket, count.Without(deleted));
        ObservedDeliveryCounts.AggregateTopics(AllEntities());
        OnPropertyChanged(nameof(CountSummary));
    }

    private void CountFailed(WatchTarget target, Exception error) =>
        BackgroundRefreshWarning?.Invoke($"Counting {target.Address.Name} ({target.Bucket}) failed. The last total is kept; refresh will retry.");
}
