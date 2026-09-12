using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationPagingTests
{
    [Fact]
    public async Task Loads_combined_page_fairly_and_chunks_broker_requests()
    {
        EntityAddress[] sources =
        [
            new(EntityKind.Subscription, "a", "orders"),
            new(EntityKind.Subscription, "b", "orders"),
            new(EntityKind.Subscription, "c", "orders")
        ];
        var service = new InMemoryMessageService(sources, messagesPerSource: 80);
        var pager = new DeliveryPager(service);
        pager.Reset(7, sources, MessageBucket.Active);

        IReadOnlyList<MessageDelivery> page = await pager.LoadNextAsync(200, CancellationToken.None);

        Assert.Equal(200, page.Count);
        Assert.Equal(
            Enumerable.Range(0, 9).Select(index => sources[index % 3]),
            page.Take(9).Select(delivery => delivery.Identity.Source));
        Assert.Equal(67, page.Count(delivery => delivery.Identity.Source == sources[0]));
        Assert.Equal(67, page.Count(delivery => delivery.Identity.Source == sources[1]));
        Assert.Equal(66, page.Count(delivery => delivery.Identity.Source == sources[2]));
        Assert.All(page, delivery => Assert.Equal(7, delivery.Identity.ConnectionGeneration));
        Assert.All(service.Calls, call => Assert.InRange(call.Take, 1, 100));
        Assert.True(pager.HasMore);
    }

    [Fact]
    public async Task Cancellation_does_not_advance_cursors_and_a_later_load_resumes()
    {
        EntityAddress source = new(EntityKind.Queue, "orders");
        var firstCallStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new InMemoryMessageService([source], messagesPerSource: 3)
        {
            WaitForFirstCall = async cancellationToken =>
            {
                firstCallStarted.SetResult(true);
                await releaseFirstCall.Task.WaitAsync(cancellationToken);
            }
        };
        var pager = new DeliveryPager(service);
        pager.Reset(1, [source], MessageBucket.Active);
        using var cancellationSource = new CancellationTokenSource();

        Task<IReadOnlyList<MessageDelivery>> canceledLoad = pager.LoadNextAsync(1, cancellationSource.Token);
        await firstCallStarted.Task;
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledLoad);

        releaseFirstCall.TrySetResult(true);
        IReadOnlyList<MessageDelivery> resumedPage = await pager.LoadNextAsync(1, CancellationToken.None);

        Assert.Single(resumedPage);
        Assert.Equal(1, resumedPage[0].Message.SequenceNumber);
        Assert.Equal(2, service.Calls.Count);
        Assert.Null(service.Calls[0].FromSequenceNumber);
        Assert.Null(service.Calls[1].FromSequenceNumber);
    }

    [Fact]
    public async Task Reset_supersedes_a_pending_load_without_committing_its_rows()
    {
        EntityAddress oldSource = new(EntityKind.Queue, "old");
        EntityAddress newSource = new(EntityKind.Queue, "new");
        var oldCallStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new InMemoryMessageService([oldSource, newSource], messagesPerSource: 1)
        {
            WaitForFirstCallIgnoringCancellation = async () =>
            {
                oldCallStarted.SetResult(true);
                await releaseOldCall.Task;
            }
        };
        var pager = new DeliveryPager(service);
        pager.Reset(10, [oldSource], MessageBucket.Active);

        Task<IReadOnlyList<MessageDelivery>> oldLoad = pager.LoadNextAsync(1, CancellationToken.None);
        await oldCallStarted.Task;
        pager.Reset(11, [newSource], MessageBucket.DeadLetter);
        releaseOldCall.SetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldLoad);
        IReadOnlyList<MessageDelivery> newPage = await pager.LoadNextAsync(1, CancellationToken.None);

        Assert.Single(newPage);
        Assert.Equal(newSource, newPage[0].Identity.Source);
        Assert.Equal(MessageBucket.DeadLetter, newPage[0].Identity.Bucket);
        Assert.Equal(11, newPage[0].Identity.ConnectionGeneration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Rejects_page_sizes_outside_the_supported_range(int pageSize)
    {
        var service = new InMemoryMessageService([], messagesPerSource: 0);
        var pager = new DeliveryPager(service);
        pager.Reset(1, [], MessageBucket.Active);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => pager.LoadNextAsync(pageSize, CancellationToken.None));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task Delivery_identity_keeps_same_sequence_distinct_per_source()
    {
        EntityAddress firstSource = new(EntityKind.Queue, "first");
        EntityAddress secondSource = new(EntityKind.Queue, "second");
        var service = new InMemoryMessageService([firstSource, secondSource], messagesPerSource: 1);
        var pager = new DeliveryPager(service);
        pager.Reset(4, [firstSource, secondSource], MessageBucket.Active);

        IReadOnlyList<MessageDelivery> page = await pager.LoadNextAsync(2, CancellationToken.None);

        Assert.Equal(2, page.Count);
        Assert.Equal(1, page[0].Message.SequenceNumber);
        Assert.Equal(1, page[1].Message.SequenceNumber);
        Assert.NotEqual(page[0].Identity, page[1].Identity);
    }

    [Fact]
    public async Task Reset_ignores_non_receiving_null_and_duplicate_sources()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityAddress topic = new(EntityKind.Topic, "events");
        var service = new InMemoryMessageService([queue], messagesPerSource: 1);
        var pager = new DeliveryPager(service);

        pager.Reset(4, [queue, queue, topic, null!], MessageBucket.Active);
        IReadOnlyList<MessageDelivery> page = await pager.LoadNextAsync(1, CancellationToken.None);

        Assert.Single(page);
        Assert.Equal(queue, page[0].Identity.Source);
        Assert.Single(service.Calls);
        Assert.DoesNotContain(service.Calls, call => call.Address.Kind == EntityKind.Topic);
    }

    [Fact]
    public async Task Terminal_long_max_sequence_drains_once_without_overflow_or_duplicate_peek()
    {
        EntityAddress source = new(EntityKind.Queue, "orders");
        var service = new InMemoryMessageService([source], messagesPerSource: 0);
        service.Set(source, InMemoryMessageService.CreateMessage(source, long.MaxValue));
        var pager = new DeliveryPager(service);
        pager.Reset(1, [source], MessageBucket.Active);

        IReadOnlyList<MessageDelivery> page = await pager.LoadNextAsync(1, CancellationToken.None);
        IReadOnlyList<MessageDelivery> next = await pager.LoadNextAsync(1, CancellationToken.None);

        Assert.Single(page);
        Assert.Empty(next);
        Assert.Single(service.Calls);
        Assert.Equal(long.MaxValue, page[0].Message.SequenceNumber);
    }

    [Fact]
    public async Task Failed_page_does_not_commit_partial_source_cursors_and_retry_peeks_every_source_again()
    {
        EntityAddress first = new(EntityKind.Queue, "first");
        EntityAddress second = new(EntityKind.Queue, "second");
        var service = new InMemoryMessageService([first, second], messagesPerSource: 1);
        service.FailingSources.Add(second);
        var pager = new DeliveryPager(service);
        pager.Reset(1, [first, second], MessageBucket.Active);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pager.LoadNextAsync(2, CancellationToken.None));

        service.FailingSources.Clear();
        IReadOnlyList<MessageDelivery> retry = await pager.LoadNextAsync(2, CancellationToken.None);

        Assert.Equal([first, second], retry.Select(delivery => delivery.Identity.Source));
        Assert.Equal(4, service.Calls.Count);
        Assert.Null(service.Calls[2].FromSequenceNumber);
        Assert.Null(service.Calls[3].FromSequenceNumber);
    }

    private sealed class InMemoryMessageService : IServiceBusMessageService
    {
        private readonly Dictionary<EntityAddress, IReadOnlyList<ExplorerMessage>> _messages;
        private int _callCount;

        public InMemoryMessageService(IReadOnlyList<EntityAddress> sources, int messagesPerSource)
        {
            _messages = sources.ToDictionary(
                source => source,
                source => (IReadOnlyList<ExplorerMessage>)Enumerable.Range(1, messagesPerSource)
                    .Select(sequence => CreateMessage(source, sequence))
                    .ToList());
        }

        public List<PeekCall> Calls { get; } = [];
        public HashSet<EntityAddress> FailingSources { get; } = [];
        public Func<CancellationToken, Task>? WaitForFirstCall { get; init; }
        public Func<Task>? WaitForFirstCallIgnoringCancellation { get; init; }

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            Calls.Add(new PeekCall(address, bucket, take, fromSequenceNumber));
            int callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber == 1 && WaitForFirstCall is not null)
            {
                await WaitForFirstCall(cancellationToken);
            }

            if (callNumber == 1 && WaitForFirstCallIgnoringCancellation is not null)
            {
                await WaitForFirstCallIgnoringCancellation();
            }

            if (FailingSources.Contains(address))
                throw new InvalidOperationException($"Peek failed for {address.Name}.");

            IReadOnlyList<ExplorerMessage> messages = _messages[address];
            return messages
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToList();
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void Set(EntityAddress source, params ExplorerMessage[] values) => _messages[source] = values;

        public static ExplorerMessage CreateMessage(EntityAddress source, long sequence)
            => new(
                $"{source.Name}-{sequence}",
                sequence,
                $"{source.Name}-{sequence}",
                $"{source.Name}-{sequence}",
                source.Name.Length + sequence.ToString().Length,
                null,
                null,
                0,
                null,
                null,
                null,
                null,
                new Dictionary<string, object?>(),
                new Dictionary<string, object?>());

        public sealed record PeekCall(
            EntityAddress Address,
            MessageBucket Bucket,
            int Take,
            long? FromSequenceNumber);
    }
}
