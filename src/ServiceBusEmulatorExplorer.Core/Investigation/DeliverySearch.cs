using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public enum DeliverySearchStopReason
{
    Completed,
    MaxDeliveries,
    TimeBudget,
    Canceled,
    SourceFailure,
    Superseded
}

public sealed record DeliverySearchSourceFailure(
    EntityAddress Source,
    MessageBucket Bucket,
    string Detail);

public sealed record DeliverySearchResult(
    IReadOnlyList<MessageDelivery> Matches,
    int ScannedDeliveries,
    bool IsComplete,
    DeliverySearchStopReason StopReason,
    IReadOnlyList<DeliverySearchSourceFailure> SourceFailures);

/// <summary>
/// Performs a bounded, non-consuming search over the receiving sources in a connection.
/// </summary>
public sealed class DeliverySearch
{
    public const int DefaultMaxDeliveries = 10_000;
    public static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(30);

    private const int BrokerBatchSize = 100;
    private readonly IServiceBusMessageService _messageService;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private List<SourceState> _sources = [];
    private int _nextSourceIndex;
    private long _configurationVersion;
    private ScanOperation? _activeScan;
    private MessageSearchQuery? _query;
    private bool _defaultMessageId;

    public DeliverySearch(IServiceBusMessageService messageService, TimeProvider? timeProvider = null)
    {
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Reset(
        long generation,
        IReadOnlyList<EntityAddress> sources,
        MessageSearchQuery query,
        bool defaultMessageId = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(query);

        List<SourceState> nextSources = sources
            .Where(source => source is not null && source.Kind != EntityKind.Topic)
            .Distinct()
            .Select(source => new SourceState(source, generation, MessageBucket.Active))
            .Concat(sources
                .Where(source => source is not null && source.Kind != EntityKind.Topic)
                .Distinct()
                .Select(source => new SourceState(source, generation, MessageBucket.DeadLetter)))
            .ToList();

        lock (_gate)
        {
            _activeScan?.Cancel();
            _configurationVersion++;
            _sources = nextSources;
            _nextSourceIndex = 0;
            _query = query;
            _defaultMessageId = defaultMessageId;
        }
    }

    public async Task<DeliverySearchResult> ScanNextAsync(
        int maxDeliveries = DefaultMaxDeliveries,
        TimeSpan? timeBudget = null,
        CancellationToken cancellationToken = default)
    {
        ValidateLimits(maxDeliveries, timeBudget);
        TimeSpan budget = timeBudget ?? DefaultTimeBudget;

        ScanOperation operation;
        lock (_gate)
        {
            if (_query is null)
            {
                throw new InvalidOperationException("Reset must be called before scanning.");
            }

            _activeScan?.Cancel();
            operation = new ScanOperation(
                _configurationVersion,
                SnapshotSources(_sources),
                _nextSourceIndex,
                _query,
                _defaultMessageId,
                cancellationToken);
            _activeScan = operation;
        }

        var matches = new List<MessageDelivery>();
        var failures = new List<DeliverySearchSourceFailure>();
        int scanned = 0;
        long startedAt = _timeProvider.GetTimestamp();
        DeliverySearchStopReason stopReason = DeliverySearchStopReason.Completed;
        bool timeBudgetElapsed = false;

        try
        {
            while (scanned < maxDeliveries && !HasBudgetElapsed(startedAt, budget))
            {
                operation.CancellationToken.ThrowIfCancellationRequested();
                if (operation.Sources.Count == 0)
                {
                    break;
                }

                bool madeProgress = false;
                int roundStartIndex = operation.NextSourceIndex;
                for (int offset = 0; offset < operation.Sources.Count && scanned < maxDeliveries; offset++)
                {
                    operation.CancellationToken.ThrowIfCancellationRequested();
                    if (HasBudgetElapsed(startedAt, budget))
                    {
                        break;
                    }

                    int sourceIndex = (roundStartIndex + offset) % operation.Sources.Count;
                    SourceState source = operation.Sources[sourceIndex];
                    if (source.Exhausted || source.FailedThisScan)
                    {
                        continue;
                    }

                    if (source.Buffer.Count == 0)
                    {
                        try
                        {
                            await FillBufferAsync(
                                source,
                                operation.CancellationToken,
                                startedAt,
                                budget).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (TimeBudgetExceededException)
                        {
                            timeBudgetElapsed = true;
                            break;
                        }
                        catch (Exception exception)
                        {
                            source.FailedThisScan = true;
                            failures.Add(CreateFailure(source, exception));
                            continue;
                        }
                    }

                    if (timeBudgetElapsed || HasBudgetElapsed(startedAt, budget))
                    {
                        timeBudgetElapsed = true;
                        break;
                    }

                    if (source.Buffer.Count == 0)
                    {
                        continue;
                    }

                    MessageDelivery delivery = source.Buffer.Dequeue();
                    if (source.ExhaustedAfterBuffer && source.Buffer.Count == 0)
                    {
                        source.Exhausted = true;
                    }
                    scanned++;
                    madeProgress = true;
                    operation.NextSourceIndex = (sourceIndex + 1) % operation.Sources.Count;

                    if (operation.Query.Matches(
                        delivery.Message.MessageId,
                        delivery.Message.CorrelationId,
                        operation.DefaultMessageId))
                    {
                        matches.Add(delivery);
                    }
                }

                if (timeBudgetElapsed || HasBudgetElapsed(startedAt, budget))
                {
                    timeBudgetElapsed = true;
                    break;
                }

                if (!madeProgress)
                {
                    break;
                }
            }

            stopReason = DetermineStopReason(operation, scanned, maxDeliveries, startedAt, budget, failures, timeBudgetElapsed);
        }
        catch (OperationCanceledException)
        {
            stopReason = DeliverySearchStopReason.Canceled;
        }

        try
        {
            bool committed = Commit(operation);
            if (!committed)
            {
                // A Reset or a newer scan has replaced this operation. Never expose
                // rows or counters produced from the superseded source configuration.
                return new DeliverySearchResult(
                    [],
                    0,
                    false,
                    DeliverySearchStopReason.Superseded,
                    []);
            }

            bool complete = committed && IsComplete(operation.Sources, failures);
            return new DeliverySearchResult(matches, scanned, complete, stopReason, failures);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeScan, operation))
                {
                    _activeScan = null;
                }
            }

            operation.Dispose();
        }
    }

    private async Task FillBufferAsync(
        SourceState source,
        CancellationToken cancellationToken,
        long startedAt,
        TimeSpan budget)
    {
        TimeSpan remaining = budget - _timeProvider.GetElapsedTime(startedAt);
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeBudgetExceededException();
        }

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            Task<IReadOnlyList<ExplorerMessage>> request = _messageService.PeekMessagesAsync(
                source.Address,
                source.Bucket,
                BrokerBatchSize,
                source.NextSequenceNumber,
                requestCancellation.Token);
            Task timeout = Task.Delay(remaining, _timeProvider, requestCancellation.Token);
            Task completed = await Task.WhenAny(request, timeout).ConfigureAwait(false);
            if (completed == timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                requestCancellation.Cancel();
                _ = request.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new TimeBudgetExceededException();
            }

            IReadOnlyList<ExplorerMessage> messages = await request.ConfigureAwait(false);

            if (messages.Count == 0)
            {
                source.Exhausted = true;
                return;
            }

            foreach (ExplorerMessage message in messages)
            {
                source.Buffer.Enqueue(new MessageDelivery(
                    new DeliveryIdentity(source.Generation, source.Address, source.Bucket, message.SequenceNumber),
                    message));
            }

            long maximumSequence = messages.Max(message => message.SequenceNumber);
            source.NextSequenceNumber = maximumSequence == long.MaxValue
                ? null
                : maximumSequence + 1;
            if (maximumSequence == long.MaxValue)
            {
                source.ExhaustedAfterBuffer = true;
            }
        }
        finally
        {
            // Cancel the delay as soon as the broker task has completed so a
            // completed search does not retain a timer for the rest of its budget.
            requestCancellation.Cancel();
        }
    }

    private static DeliverySearchSourceFailure CreateFailure(SourceState source, Exception exception)
    {
        return new DeliverySearchSourceFailure(
            source.Address,
            source.Bucket,
            $"Peeking messages failed ({exception.GetType().Name}).");
    }

    private bool Commit(ScanOperation operation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_activeScan, operation)
                || operation.ConfigurationVersion != _configurationVersion)
            {
                return false;
            }

            _sources = operation.Sources;
            _nextSourceIndex = operation.NextSourceIndex;
            return true;
        }
    }

    private DeliverySearchStopReason DetermineStopReason(
        ScanOperation operation,
        int scanned,
        int maxDeliveries,
        long startedAt,
        TimeSpan budget,
        IReadOnlyList<DeliverySearchSourceFailure> failures,
        bool timeBudgetElapsed)
    {
        if (IsComplete(operation.Sources, failures))
        {
            return DeliverySearchStopReason.Completed;
        }

        if (scanned >= maxDeliveries)
        {
            return DeliverySearchStopReason.MaxDeliveries;
        }

        if (timeBudgetElapsed || _timeProvider.GetElapsedTime(startedAt) >= budget)
        {
            return DeliverySearchStopReason.TimeBudget;
        }

        return failures.Count > 0
            ? DeliverySearchStopReason.SourceFailure
            : DeliverySearchStopReason.Completed;
    }

    private bool HasBudgetElapsed(long startedAt, TimeSpan budget) =>
        _timeProvider.GetElapsedTime(startedAt) >= budget;

    private sealed class TimeBudgetExceededException : Exception
    {
    }

    private static bool IsComplete(
        IReadOnlyList<SourceState> sources,
        IReadOnlyList<DeliverySearchSourceFailure> failures)
    {
        return failures.Count == 0
            && sources.All(source => source.Exhausted && source.Buffer.Count == 0);
    }

    private static List<SourceState> SnapshotSources(IReadOnlyList<SourceState> sources) =>
        sources.Select(source => source.Clone()).ToList();

    private static void ValidateLimits(int maxDeliveries, TimeSpan? timeBudget)
    {
        if (maxDeliveries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDeliveries), maxDeliveries, "Maximum deliveries must be positive.");
        }

        if (timeBudget is { } budget && budget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeBudget), timeBudget, "Time budget must be positive.");
        }
    }

    private sealed class ScanOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellationSource;

        public ScanOperation(
            long configurationVersion,
            List<SourceState> sources,
            int nextSourceIndex,
            MessageSearchQuery query,
            bool defaultMessageId,
            CancellationToken cancellationToken)
        {
            ConfigurationVersion = configurationVersion;
            Sources = sources;
            NextSourceIndex = nextSourceIndex;
            Query = query;
            DefaultMessageId = defaultMessageId;
            _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public long ConfigurationVersion { get; }
        public List<SourceState> Sources { get; }
        public int NextSourceIndex { get; set; }
        public MessageSearchQuery Query { get; }
        public bool DefaultMessageId { get; }
        public CancellationToken CancellationToken => _cancellationSource.Token;

        public void Cancel() => _cancellationSource.Cancel();

        public void Dispose() => _cancellationSource.Dispose();
    }

    private sealed class SourceState
    {
        public SourceState(EntityAddress address, long generation, MessageBucket bucket)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Generation = generation;
            Bucket = bucket;
        }

        private SourceState(SourceState source)
        {
            Address = source.Address;
            Generation = source.Generation;
            Bucket = source.Bucket;
            NextSequenceNumber = source.NextSequenceNumber;
            Exhausted = source.Exhausted;
            ExhaustedAfterBuffer = source.ExhaustedAfterBuffer;
            Buffer = new Queue<MessageDelivery>(source.Buffer);
        }

        public EntityAddress Address { get; }
        public long Generation { get; }
        public MessageBucket Bucket { get; }
        public long? NextSequenceNumber { get; set; }
        public bool Exhausted { get; set; }
        public bool ExhaustedAfterBuffer { get; set; }
        public bool FailedThisScan { get; set; }
        public Queue<MessageDelivery> Buffer { get; } = [];

        public SourceState Clone() => new(this);
    }
}
