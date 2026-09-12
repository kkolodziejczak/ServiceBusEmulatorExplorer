using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationSearchTests
{
    [Fact]
    public async Task Search_scans_active_and_dead_letter_sources_fairly_and_resumes_buffered_deliveries()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityAddress subscription = new(EntityKind.Subscription, "billing", "events");
        var service = new InMemoryMessageService();
        service.Add(queue, MessageBucket.Active, Message("q-a-1", 1));
        service.Add(queue, MessageBucket.DeadLetter, Message("q-d-2", 2));
        service.Add(subscription, MessageBucket.Active, Message("s-a-3", 3));
        service.Add(subscription, MessageBucket.DeadLetter, Message("s-d-4", 4));

        var search = new DeliverySearch(service);
        search.Reset(17, [queue, subscription], Parse("*"), defaultMessageId: true);

        DeliverySearchResult first = await search.ScanNextAsync(2, TimeSpan.FromMinutes(1));
        DeliverySearchResult second = await search.ScanNextAsync(2, TimeSpan.FromMinutes(1));

        Assert.Equal(2, first.ScannedDeliveries);
        Assert.Equal(2, second.ScannedDeliveries);
        Assert.Equal(
            [queue, subscription],
            first.Matches.Select(item => item.Identity.Source));
        Assert.Equal(
            [MessageBucket.Active, MessageBucket.Active],
            first.Matches.Select(item => item.Identity.Bucket));
        Assert.All(first.Matches.Concat(second.Matches), item => Assert.Equal(17, item.Identity.ConnectionGeneration));
        // Hitting the delivery budget cannot certify exhaustion until every source has returned an empty page.
        Assert.False(second.IsComplete);
        DeliverySearchResult completion = await search.ScanNextAsync(10, TimeSpan.FromMinutes(1));
        Assert.True(completion.IsComplete);
        Assert.Empty(completion.Matches);
    }

    [Fact]
    public async Task Failed_source_is_reported_as_partial_and_retried_without_advancing_its_cursor()
    {
        EntityAddress source = new(EntityKind.Queue, "orders");
        var service = new InMemoryMessageService
        {
            FailuresRemaining = 1
        };
        service.Add(source, MessageBucket.Active, Message("order-1", 10));

        var search = new DeliverySearch(service);
        search.Reset(3, [source], Parse("order-1"), defaultMessageId: true);

        DeliverySearchResult failed = await search.ScanNextAsync(10, TimeSpan.FromMinutes(1));
        DeliverySearchResult resumed = await search.ScanNextAsync(10, TimeSpan.FromMinutes(1));

        Assert.False(failed.IsComplete);
        Assert.Single(failed.SourceFailures);
        Assert.Empty(failed.Matches);
        Assert.True(resumed.IsComplete);
        Assert.Single(resumed.Matches);
        Assert.Equal(10, resumed.Matches[0].Identity.SequenceNumber);
        Assert.Null(service.Calls[0].FromSequenceNumber);
        Assert.Null(service.Calls[1].FromSequenceNumber);
    }

    [Fact]
    public async Task Topic_metadata_is_ignored_and_cancellation_preserves_progress()
    {
        EntityAddress topic = new(EntityKind.Topic, "events");
        EntityAddress queue = new(EntityKind.Queue, "orders");
        var service = new InMemoryMessageService();
        service.Add(queue, MessageBucket.Active, Message("order-1", 1));

        var search = new DeliverySearch(service);
        search.Reset(9, [topic, queue], Parse("order-1"), defaultMessageId: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        DeliverySearchResult canceled = await search.ScanNextAsync(10, TimeSpan.FromMinutes(1), cancellation.Token);
        DeliverySearchResult resumed = await search.ScanNextAsync(10, TimeSpan.FromMinutes(1));

        Assert.False(canceled.IsComplete);
        Assert.Equal(DeliverySearchStopReason.Canceled, canceled.StopReason);
        Assert.True(resumed.IsComplete);
        Assert.Single(resumed.Matches);
        Assert.DoesNotContain(service.Calls, call => call.Address.Kind == EntityKind.Topic);
    }

    [Fact]
    public async Task Elapsed_time_budget_interrupts_a_blocked_peek_and_returns_partial_status()
    {
        EntityAddress source = new(EntityKind.Queue, "orders");
        var service = new BlockingMessageService();
        var search = new DeliverySearch(service, TimeProvider.System);
        search.Reset(4, [source], Parse("*"), defaultMessageId: true);

        Task<DeliverySearchResult> scan = search.ScanNextAsync(10, TimeSpan.FromMilliseconds(100));
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        DeliverySearchResult result = await scan;

        Assert.False(result.IsComplete);
        Assert.Equal(DeliverySearchStopReason.TimeBudget, result.StopReason);
        Assert.Equal(0, result.ScannedDeliveries);
        Assert.True(service.CancellationObserved);
    }

    [Fact]
    public async Task Reset_supersedes_inflight_scan_without_returning_stale_rows()
    {
        EntityAddress source = new(EntityKind.Queue, "orders");
        var service = new BlockingMessageService();
        var search = new DeliverySearch(service);
        search.Reset(1, [source], Parse("*"), defaultMessageId: true);

        Task<DeliverySearchResult> oldScan = search.ScanNextAsync(10, TimeSpan.FromMinutes(1));
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        search.Reset(2, [source], Parse("*"), defaultMessageId: true);

        DeliverySearchResult result = await oldScan;

        Assert.Equal(DeliverySearchStopReason.Superseded, result.StopReason);
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.ScannedDeliveries);
        Assert.False(result.IsComplete);
    }

    private static MessageSearchQuery Parse(string text)
    {
        Assert.True(MessageSearchQuery.TryParse(text, out MessageSearchQuery? query, out string error), error);
        return query!;
    }

    private static ExplorerMessage Message(string id, long sequence) => new(
        id,
        sequence,
        id,
        id,
        id.Length,
        null,
        null,
        0,
        null,
        null,
        null,
        null,
        new Dictionary<string, object?>(),
        new Dictionary<string, object?>());

    private sealed class InMemoryMessageService : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> _messages = [];

        public int FailuresRemaining { get; set; }
        public List<PeekCall> Calls { get; } = [];

        public void Add(EntityAddress address, MessageBucket bucket, params ExplorerMessage[] messages) =>
            _messages[(address, bucket)] = messages;

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            Calls.Add(new PeekCall(address, bucket, take, fromSequenceNumber));
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("simulated failure");
            }

            IReadOnlyList<ExplorerMessage> messages = _messages.TryGetValue((address, bucket), out IReadOnlyList<ExplorerMessage>? found)
                ? found
                : [];
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(messages
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                .Take(take)
                .ToList());
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public sealed record PeekCall(
            EntityAddress Address,
            MessageBucket Bucket,
            int Take,
            long? FromSequenceNumber);
    }

    private sealed class BlockingMessageService : IServiceBusMessageService
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            Started.SetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
            }

            return [];
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
