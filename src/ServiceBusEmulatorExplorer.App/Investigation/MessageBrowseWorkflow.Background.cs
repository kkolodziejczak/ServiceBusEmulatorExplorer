using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class MessageBrowseWorkflow
{
    private sealed record CachedPage(DeliveryPager Pager, IReadOnlyList<MessageDelivery> Deliveries);
    private readonly Dictionary<WatchTarget, CachedPage> backgroundPages = [];
    private Dictionary<WatchTarget, EntityAddress[]> watchedScopes = [];
    private IReadOnlyList<WatchPreference> watchRules = [];
    private CancellationTokenSource? backgroundCancellation;
    private Task backgroundRefresh = Task.CompletedTask;
    public bool HasWatchedTargets => watchedScopes.Count > 0;
    public event Action<string>? BackgroundRefreshWarning;

    public void SetWatchRules(IReadOnlyList<WatchPreference> rules)
    {
        if (watchRules.SequenceEqual(rules)) return;
        CancelBackgroundRefresh();
        watchRules = rules.ToArray();
        ReconcileWatchedScopes();
        RememberWatchedPage();
    }

    private void ReconcileWatchedScopes()
    {
        // Use reconciled nodes: an incomplete discovery must not remove previously known targets.
        var snapshot = new EntityDiscoverySnapshot(AllEntities().Select(node => node.Observation!).ToArray(),
            DateTimeOffset.UtcNow, true, []);
        var targets = new WatchScopeResolver().Resolve(watchRules, snapshot).ToHashSet();
        var scopes = targets.ToDictionary(target => target, target => new[] { target.Address });
        foreach (var topic in AllEntities().Where(node => node.Kind == nameof(EntityKind.Topic)))
        {
            var sources = topic.Children.Select(child => child.Address!).ToArray();
            foreach (var messageBucket in new[] { MessageBucket.Active, MessageBucket.DeadLetter })
                if (sources.Length > 0 && sources.All(source => targets.Contains(new(source, messageBucket))))
                    scopes.Add(new(topic.Address!, messageBucket), sources);
        }
        foreach (var key in backgroundPages.Keys.ToArray())
            if (!scopes.TryGetValue(key, out var sources) || !watchedScopes.TryGetValue(key, out var previous)
                || !sources.SequenceEqual(previous)) backgroundPages.Remove(key);
        watchedScopes = scopes;
        OnPropertyChanged(nameof(HasWatchedTargets));
    }

    private void RememberWatchedPage()
    {
        if (selectedEntity?.Address is not { } address || pager is null) return;
        var key = new WatchTarget(address, bucket);
        if (watchedScopes.ContainsKey(key))
            backgroundPages[key] = new(pager, Messages.Where(row => row.ObservationDetail.Length == 0).Select(row => row.Delivery).ToArray());
    }

    public Task RefreshBackgroundAsync(bool refreshSelected = true)
    {
        if (session is null || !backgroundRefresh.IsCompleted) return backgroundRefresh;
        backgroundRefresh = RefreshBackgroundCoreAsync(session, generation, refreshSelected);
        return backgroundRefresh;
    }

    internal void RestoreWatchedMessages()
    {
        if (busy || selectedEntity?.Address is not { } address
            || !backgroundPages.TryGetValue(new(address, bucket), out var cached)) return;
        pager = cached.Pager;
        ApplyRefreshedDeliveries(cached.Deliveries);
        NotifyScope();
    }

    private async Task RefreshBackgroundCoreAsync(BrokerSession capturedSession, long capturedGeneration, bool refreshSelected)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        backgroundCancellation = cancellation;
        try
        {
            var discovery = await capturedSession.Browser.DiscoverAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (session != capturedSession || generation != capturedGeneration) return;
            ApplySnapshot(discovery);
            var scopes = watchedScopes.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (refreshSelected && !busy && selectedEntity?.Address is { } address)
                scopes.TryAdd(new(address, bucket), selectedEntity.Kind == nameof(EntityKind.Topic)
                    ? selectedEntity.Children.Select(child => child.Address!).ToArray() : [address]);
            foreach (var (key, sources) in scopes)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                bool visible = !busy && selectedEntity?.Address == key.Address && bucket == key.Bucket;
                if (busy && selectedEntity?.Address == key.Address && bucket == key.Bucket) continue;
                int selectionVersion = version;
                backgroundPages.TryGetValue(key, out var previous);
                int pageSize = key.Address.Kind switch
                {
                    EntityKind.Topic => preferences.TopicPageSize,
                    EntityKind.Subscription => preferences.SubscriptionPageSize,
                    _ => preferences.QueuePageSize
                };
                int depth = Math.Max(pageSize, visible ? Messages.Count : previous?.Deliveries.Count ?? 0);
                var refreshed = new DeliveryPager(capturedSession.Messages);
                refreshed.Reset(capturedGeneration, sources, key.Bucket);
                var deliveries = new List<MessageDelivery>();
                try
                {
                    while (deliveries.Count < depth && refreshed.HasMore)
                    {
                        var page = await refreshed.LoadNextAsync(Math.Min(200, depth - deliveries.Count), cancellation.Token);
                        deliveries.AddRange(page);
                        if (page.Count == 0) break;
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (session != capturedSession || generation != capturedGeneration) return;
                    if (backgroundPages.GetValueOrDefault(key) != previous) continue;
                    deliveries.RemoveAll(delivery => deleted.Contains(delivery.Identity));
                    if (watchedScopes.TryGetValue(key, out var currentSources) && currentSources.SequenceEqual(sources))
                    {
                        backgroundPages[key] = new(refreshed, deliveries);
                        CaptureBackgroundCounts(key, deliveries, !refreshed.HasMore);
                    }
                    if (refreshSelected && visible && selectionVersion == version && !busy)
                    {
                        pager = refreshed;
                        ApplyRefreshedDeliveries(deliveries);
                        NotifyScope();
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    BackgroundRefreshWarning?.Invoke($"Background refresh failed for {key.Address.Name} ({key.Bucket}). The last loaded messages are retained; refresh will retry.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            BackgroundRefreshWarning?.Invoke("Background refresh could not discover the namespace. The last loaded messages are retained; refresh will retry.");
        }
        finally
        {
            if (ReferenceEquals(backgroundCancellation, cancellation)) backgroundCancellation = null;
        }
    }

    private void CaptureBackgroundCounts(WatchTarget target, IReadOnlyList<MessageDelivery> deliveries, bool complete)
    {
        if (!UsesObservedCounts || target.Address.Kind == EntityKind.Topic) return;
        var nodes = AllEntities().ToArray();
        var node = nodes.FirstOrDefault(candidate => candidate.Address == target.Address);
        if (node is null) return;
        node.SetObservedCount(target.Bucket, new(deliveries.Select(delivery => delivery.Identity).ToHashSet(), complete, DateTimeOffset.UtcNow));
        ObservedDeliveryCounts.AggregateTopics(nodes);
    }

    public void CancelBackgroundRefresh() => backgroundCancellation?.Cancel();

    public async Task StopBackgroundRefreshAsync()
    {
        CancelBackgroundRefresh();
        await backgroundRefresh;
    }
}
