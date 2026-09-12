using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public sealed record WatchPollFailure(WatchTarget Target, string Message);

public sealed record WatchPollResult(
    IReadOnlyList<MessageDelivery> Arrivals,
    int ScannedDeliveries,
    int BaselinesPending,
    bool HasPendingScans,
    IReadOnlyList<WatchPollFailure> Failures);

/// <summary>Non-consuming, resumable full scans with a fresh baseline for each watched source.</summary>
public sealed class DeliveryWatch(IServiceBusMessageService messages)
{
    private readonly IServiceBusMessageService messages = messages ?? throw new ArgumentNullException(nameof(messages));
    private readonly object gate = new();
    private List<Source> sources = [];
    private long generation;
    private long version;
    private int nextSource;
    private CancellationTokenSource? active;

    public void SetTargets(long connectionGeneration, IReadOnlyList<WatchTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var distinct = targets.Distinct().ToArray();
        foreach (var target in distinct)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.Address is null || target.Address.Kind is not (EntityKind.Queue or EntityKind.Subscription)
                || !Enum.IsDefined(target.Bucket))
                throw new ArgumentException("Watch targets must identify a receiving entity and message bucket.", nameof(targets));
        }
        lock (gate)
        {
            if (connectionGeneration == generation && sources.Select(source => source.Target).SequenceEqual(distinct)) return;
            active?.Cancel();
            active = null;
            version++;
            var retained = connectionGeneration == generation ? sources.ToDictionary(source => source.Target) : [];
            sources = distinct.Select(target => retained.GetValueOrDefault(target) ?? new Source(target)).ToList();
            foreach (var source in sources) source.RoundFinished = false;
            generation = connectionGeneration;
            nextSource = 0;
        }
    }

    public async Task<WatchPollResult> PollAsync(int maxDeliveries, CancellationToken cancellationToken)
    {
        if (maxDeliveries <= 0) throw new ArgumentOutOfRangeException(nameof(maxDeliveries));
        List<Source> working;
        long pollVersion;
        long pollGeneration;
        int cursor;
        CancellationTokenSource operation;
        lock (gate)
        {
            if (active is not null) throw new InvalidOperationException("A Watch poll is already running.");
            active = operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            working = sources.Select(source => source.Copy()).ToList();
            if (working.All(source => source.RoundFinished))
                foreach (var source in working) source.RoundFinished = false;
            pollVersion = version;
            pollGeneration = generation;
            cursor = nextSource;
        }
        using (operation)
        {
            try
            {
                var arrivals = new List<MessageDelivery>();
                var failures = new List<WatchPollFailure>();
                var finished = working.Where(source => source.RoundFinished).Select(source => source.Target).ToHashSet();
                int scanned = 0;
                while (scanned < maxDeliveries && finished.Count < working.Count)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    Source source = working[cursor];
                    cursor = (cursor + 1) % working.Count;
                    if (finished.Contains(source.Target)) continue;
                    IReadOnlyList<ExplorerMessage> page;
                    try
                    {
                        page = await messages.PeekMessagesAsync(source.Target.Address, source.Target.Bucket,
                            Math.Min(100, maxDeliveries - scanned), source.NextSequence, operation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        failures.Add(new(source.Target, "Watch could not inspect this source. The scan will be retried."));
                        source.ScanFailed = true;
                        source.RoundFinished = true;
                        finished.Add(source.Target);
                        continue;
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    source.ScanFailed = false;
                    scanned += page.Count;
                    if (page.Count == 0)
                    {
                        source.CompleteScan();
                        finished.Add(source.Target);
                        continue;
                    }
                    foreach (var message in page)
                    {
                        if (message.SequenceNumber < source.NextSequence)
                            throw new InvalidOperationException("Watch peek returned a message before the requested cursor.");
                        source.Current.Add(message.SequenceNumber);
                        if (source.Known.Add(message.SequenceNumber) && source.BaselineEstablished)
                            arrivals.Add(new(new(pollGeneration, source.Target.Address, source.Target.Bucket, message.SequenceNumber), message));
                    }
                    long last = page.Max(message => message.SequenceNumber);
                    if (last == long.MaxValue)
                    {
                        source.CompleteScan();
                        finished.Add(source.Target);
                    }
                    else source.NextSequence = last + 1;
                }
                lock (gate)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (pollVersion != version || !ReferenceEquals(active, operation))
                        throw new OperationCanceledException("Watch configuration changed during the scan.");
                    sources = working;
                    nextSource = cursor;
                    return new(arrivals, scanned, working.Count(source => !source.BaselineEstablished),
                        working.Any(source => !source.RoundFinished || source.ScanFailed), failures);
                }
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(active, operation)) active = null;
                }
            }
        }
    }

    private sealed class Source(WatchTarget target)
    {
        public WatchTarget Target { get; } = target;
        public HashSet<long> Known { get; private set; } = [];
        public HashSet<long> Current { get; private set; } = [];
        public bool BaselineEstablished { get; private set; }
        public long NextSequence { get; set; }
        public bool RoundFinished { get; set; }
        public bool ScanFailed { get; set; }

        public Source Copy() => new(Target)
        {
            Known = new(Known), Current = new(Current),
            BaselineEstablished = BaselineEstablished, NextSequence = NextSequence,
            RoundFinished = RoundFinished, ScanFailed = ScanFailed
        };

        public void CompleteScan()
        {
            Known = Current;
            Current = [];
            NextSequence = 0;
            BaselineEstablished = true;
            RoundFinished = true;
        }
    }
}
