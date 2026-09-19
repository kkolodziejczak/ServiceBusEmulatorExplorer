using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>
/// Pages peeked deliveries across one or more receiving sources.
/// </summary>
public sealed class DeliveryPager
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
    private const int BrokerPageSize = 100;

    private readonly IServiceBusMessageService _messageService;
    private readonly object _gate = new();
    private List<SourceState> _sources = [];
    private int _nextSourceIndex;
    private long _configurationVersion;
    private LoadOperation? _activeLoad;

    public DeliveryPager(IServiceBusMessageService messageService)
    {
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
    }

    public bool HasMore
    {
        get
        {
            lock (_gate)
            {
                return _sources.Any(source => !source.Exhausted || source.Buffer.Count > 0);
            }
        }
    }

    public void Reset(
        long generation,
        IReadOnlyList<EntityAddress> sources,
        MessageBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(sources);
        List<SourceState> nextSources = sources
            .Where(source => source is not null && source.Kind != EntityKind.Topic)
            .Distinct()
            .Select(source => new SourceState(source, bucket, generation))
            .ToList();

        lock (_gate)
        {
            _activeLoad?.Cancel();
            _configurationVersion++;
            _sources = nextSources;
            _nextSourceIndex = 0;
        }
    }

    public async Task<IReadOnlyList<MessageDelivery>> LoadNextAsync(
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePageSize(pageSize);

        LoadOperation operation;
        lock (_gate)
        {
            _activeLoad?.Cancel();
            operation = new LoadOperation(
                _configurationVersion,
                SnapshotSources(_sources),
                _nextSourceIndex,
                cancellationToken);
            _activeLoad = operation;
        }

        try
        {
            IReadOnlyList<MessageDelivery> page = await LoadPageAsync(operation, pageSize).ConfigureAwait(false);
            operation.CancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (!ReferenceEquals(_activeLoad, operation)
                    || operation.ConfigurationVersion != _configurationVersion)
                {
                    throw new OperationCanceledException(operation.CancellationToken);
                }

                _sources = operation.Sources;
                _nextSourceIndex = operation.NextSourceIndex;
            }

            return page;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeLoad, operation))
                {
                    _activeLoad = null;
                }
            }

            operation.Dispose();
        }
    }

    private async Task<IReadOnlyList<MessageDelivery>> LoadPageAsync(
        LoadOperation operation,
        int pageSize)
    {
        List<MessageDelivery> page = new(pageSize);

        while (page.Count < pageSize)
        {
            operation.CancellationToken.ThrowIfCancellationRequested();
            bool madeProgress = false;
            int roundStartIndex = operation.NextSourceIndex;

            for (int offset = 0; offset < operation.Sources.Count && page.Count < pageSize; offset++)
            {
                operation.CancellationToken.ThrowIfCancellationRequested();
                if (operation.Sources.Count == 0)
                {
                    break;
                }

                int sourceIndex = (roundStartIndex + offset) % operation.Sources.Count;
                SourceState source = operation.Sources[sourceIndex];
                if (source.Buffer.Count == 0 && !source.Exhausted)
                {
                    await FillBufferAsync(
                        source,
                        Math.Min(BrokerPageSize, pageSize),
                        operation.CancellationToken).ConfigureAwait(false);
                }

                if (source.Buffer.Count == 0)
                {
                    continue;
                }

                page.Add(source.Buffer.Dequeue());
                if (source.ExhaustedAfterBuffer && source.Buffer.Count == 0)
                    source.Exhausted = true;
                operation.NextSourceIndex = (sourceIndex + 1) % operation.Sources.Count;
                madeProgress = true;
            }

            if (!madeProgress)
            {
                break;
            }
        }

        return page;
    }

    private async Task FillBufferAsync(
        SourceState source,
        int take,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExplorerMessage> messages = await _messageService.PeekMessagesAsync(
            source.Address,
            source.Bucket,
            take,
            source.NextSequenceNumber,
            cancellationToken).ConfigureAwait(false);

        if (messages.Count == 0)
        {
            source.Exhausted = true;
            return;
        }

        foreach (ExplorerMessage message in messages)
        {
            source.Buffer.Enqueue(new MessageDelivery(
                new DeliveryIdentity(
                    source.Generation,
                    source.Address,
                    source.Bucket,
                    message.SequenceNumber),
                message));
        }

        long maximumSequence = messages.Max(message => message.SequenceNumber);
        source.NextSequenceNumber = maximumSequence == long.MaxValue
            ? null
            : maximumSequence + 1;
        if (maximumSequence == long.MaxValue)
            source.ExhaustedAfterBuffer = true;
    }

    private static List<SourceState> SnapshotSources(IReadOnlyList<SourceState> sources)
    {
        return sources.Select(source => source.Clone()).ToList();
    }

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be between 1 and {MaximumPageSize}.");
        }
    }

    private sealed class LoadOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellationSource;

        public LoadOperation(
            long configurationVersion,
            List<SourceState> sources,
            int nextSourceIndex,
            CancellationToken cancellationToken)
        {
            ConfigurationVersion = configurationVersion;
            Sources = sources;
            NextSourceIndex = nextSourceIndex;
            _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public long ConfigurationVersion { get; }
        public List<SourceState> Sources { get; }
        public int NextSourceIndex { get; set; }
        public CancellationToken CancellationToken => _cancellationSource.Token;

        public void Cancel() => _cancellationSource.Cancel();

        public void Dispose() => _cancellationSource.Dispose();
    }

    private sealed class SourceState
    {
        public SourceState(EntityAddress address, MessageBucket bucket, long generation)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Bucket = bucket;
            Generation = generation;
        }

        private SourceState(SourceState source)
        {
            Address = source.Address;
            Bucket = source.Bucket;
            Generation = source.Generation;
            NextSequenceNumber = source.NextSequenceNumber;
            Exhausted = source.Exhausted;
            ExhaustedAfterBuffer = source.ExhaustedAfterBuffer;
            Buffer = new Queue<MessageDelivery>(source.Buffer);
        }

        public EntityAddress Address { get; }
        public MessageBucket Bucket { get; }
        public long Generation { get; }
        public long? NextSequenceNumber { get; set; }
        public bool Exhausted { get; set; }
        public bool ExhaustedAfterBuffer { get; set; }
        public Queue<MessageDelivery> Buffer { get; } = [];

        public SourceState Clone() => new(this);
    }
}
