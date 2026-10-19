using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

// Emulator counts: an entity is counted in full when first opened, then recounted on every refresh.
public sealed partial class MessageBrowseWorkflow
{
    private static readonly MessageBucket[] CountedBuckets = [MessageBucket.Active, MessageBucket.DeadLetter];
    private readonly HashSet<EntityAddress> opened = [];
    // The node each count was requested for: a result for a removed and recreated entity is dropped.
    private readonly Dictionary<EntityAddress, EntityNode> countedNodes = [];
    private ObservedCountScanner? counter;

    private void CountFirstTime(EntityNode node)
    {
        if (counter is null) return;
        opened.Add(node.Address!);
        if (CountEmptyTopic(node)) return;
        counter.RequestFirst(CountTargets(CountSources(node), uncountedOnly: true));
    }

    // A topic without subscriptions has nothing to peek; its total is zero.
    private static bool CountEmptyTopic(EntityNode node)
    {
        if (node.Kind != nameof(EntityKind.Topic) || node.Children.Count > 0) return false;
        foreach (var countedBucket in CountedBuckets)
            node.SetObservedCount(countedBucket, node.GetObservedCount(countedBucket) ?? new(new HashSet<DeliveryIdentity>(), DateTimeOffset.UtcNow));
        return true;
    }

    // Recounts every opened entity (including a failed first count and subscriptions added to an
    // opened topic) and watched entities, which the user asked to follow without opening them.
    private void Recount()
    {
        if (counter is null) return;
        var watched = watchedScopes.Values.SelectMany(addresses => addresses).ToHashSet();
        var sources = new List<EntityNode>();
        foreach (var node in AllEntities().ToArray())
        {
            if (node.Address is not { } address) continue;
            if (opened.Contains(address) && !CountEmptyTopic(node)) sources.AddRange(CountSources(node));
            if (watched.Contains(address)) sources.Add(node);
        }
        counter.RequestLater(CountTargets(sources.Distinct(), uncountedOnly: false));
    }

    private static EntityNode[] CountSources(EntityNode node) =>
        node.Kind == nameof(EntityKind.Topic) ? node.Children.ToArray() : [node];

    private WatchTarget[] CountTargets(IEnumerable<EntityNode> sources, bool uncountedOnly)
    {
        var targets = new List<WatchTarget>();
        foreach (var source in sources)
        {
            countedNodes[source.Address!] = source;
            targets.AddRange(CountedBuckets
                .Where(countedBucket => !uncountedOnly || source.GetObservedCount(countedBucket) is null)
                .Select(countedBucket => new WatchTarget(source.Address!, countedBucket)));
        }
        return targets.ToArray();
    }

    private void PublishCount(WatchTarget target, ObservedDeliveryCount count)
    {
        if (AllEntities().FirstOrDefault(node => node.Address == target.Address) is not { } node
            || !ReferenceEquals(countedNodes.GetValueOrDefault(target.Address), node)) return;
        node.SetObservedCount(target.Bucket, count.Without(deleted));
        ObservedDeliveryCounts.AggregateTopics(AllEntities());
        OnPropertyChanged(nameof(CountSummary));
    }

    private void CountFailed(WatchTarget target, Exception error) =>
        BackgroundRefreshWarning?.Invoke($"Counting {target.Address.Name} ({target.Bucket}) failed. The last total is kept; refresh will retry.");
}
