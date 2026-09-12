using System.Collections.ObjectModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>Session-owned Watch polling. Call from the workspace's synchronization context.</summary>
public sealed class MessageWatchWorkflow(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly WatchScopeResolver resolver = new();
    private Run? current;
    public ObservableCollection<MessageDelivery> PendingArrivals { get; } = [];
    public event Action<WatchPollResult>? Polled;
    public event Action<string>? Warning;

    public void Start(BrokerSession session, long generation, IReadOnlyList<WatchPreference> rules)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rules);
        if (current is not null) throw new InvalidOperationException("Stop the previous Watch session before starting another.");
        var run = new Run(session, generation, rules.ToArray());
        current = run;
        run.Completion = RunAsync(run);
    }

    private async Task RunAsync(Run run)
    {
        // Publish the run handle before any synchronous fake or SDK operation can finish.
        await Task.Yield();
        try
        {
            while (!run.Lifetime.IsCancellationRequested)
            {
                using var iteration = CancellationTokenSource.CreateLinkedTokenSource(run.Lifetime.Token);
                run.Iteration = iteration;
                try
                {
                    if (run.Rules.Any(rule => rule.Active == true || rule.DeadLetter == true))
                        await PollAsync(run, iteration.Token);
                    await Task.Delay(TimeSpan.FromSeconds(15), clock, iteration.Token);
                }
                catch (OperationCanceledException) when (iteration.IsCancellationRequested) { }
                finally { run.Iteration = null; }
            }
        }
        finally { run.Lifetime.Dispose(); }
    }

    private async Task PollAsync(Run run, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var snapshot = await run.Session.Browser.DiscoverAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(current, run)) return;
            // Missing entities in a partial discovery are not evidence of removal.
            run.Snapshot = MergeDiscovery(run.Snapshot, snapshot);
            run.Engine.SetTargets(run.Generation, resolver.Resolve(run.Rules, run.Snapshot));
            if (!snapshot.IsComplete) Warning?.Invoke("Watch discovery is incomplete. Previously known sources remain watched; discovery will be retried.");
            int remaining = 10_000;
            do
            {
                // Commit each page so a later timeout cannot erase an entire large scan's progress.
                var result = await run.Engine.PollAsync(Math.Min(100, remaining), operation.Token);
                // The engine has committed these observations. A deadline firing while this
                // continuation waits for the UI thread must not discard an already-seen arrival.
                if (!ReferenceEquals(current, run)) return;
                foreach (var arrival in result.Arrivals)
                    if (!PendingArrivals.Any(existing => existing.Identity == arrival.Identity)) PendingArrivals.Add(arrival);
                Polled?.Invoke(result);
                remaining -= result.ScannedDeliveries;
                if (!result.HasPendingScans || result.Failures.Count > 0 || result.ScannedDeliveries == 0) break;
            } while (remaining > 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(current, run)) Warning?.Invoke("Watch inspection timed out. The scan will be retried.");
        }
        catch (Exception)
        {
            if (ReferenceEquals(current, run)) Warning?.Invoke("Watch could not refresh or inspect the namespace. The scan will be retried.");
        }
    }

    private static EntityDiscoverySnapshot MergeDiscovery(EntityDiscoverySnapshot previous, EntityDiscoverySnapshot latest)
    {
        if (latest.IsComplete) return latest;
        var merged = previous.Entities.Concat(latest.Entities)
            .GroupBy(observation => (observation.Entity.Kind, observation.Entity.TopicName, observation.Entity.Name))
            .Select(group => group.Last()).ToArray();
        return latest with { Entities = merged };
    }

    public void UpdateRules(IReadOnlyList<WatchPreference> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (current is not { } run || run.Rules.SequenceEqual(rules)) return;
        run.Rules = rules.ToArray();
        run.Engine.SetTargets(run.Generation, resolver.Resolve(run.Rules, run.Snapshot));
        run.Iteration?.Cancel();
    }

    public async Task StopAsync()
    {
        var run = current;
        current = null;
        PendingArrivals.Clear();
        if (run is null) return;
        run.Lifetime.Cancel();
        await run.Completion;
    }

    private sealed class Run(BrokerSession session, long generation, IReadOnlyList<WatchPreference> rules)
    {
        public BrokerSession Session { get; } = session;
        public long Generation { get; } = generation;
        public DeliveryWatch Engine { get; } = new(session.Messages);
        public EntityDiscoverySnapshot Snapshot { get; set; } = session.Snapshot;
        public IReadOnlyList<WatchPreference> Rules { get; set; } = rules;
        public CancellationTokenSource Lifetime { get; } = new();
        public CancellationTokenSource? Iteration { get; set; }
        public Task Completion { get; set; } = Task.CompletedTask;
    }
}
