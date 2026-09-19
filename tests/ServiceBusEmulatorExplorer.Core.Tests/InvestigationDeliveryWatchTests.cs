using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationDeliveryWatchTests
{
    private static readonly WatchTarget Queue = new(new(EntityKind.Queue, "orders"), MessageBucket.Active);
    private static readonly WatchTarget Dlq = new(new(EntityKind.Subscription, "billing", "orders"), MessageBucket.DeadLetter);

    [Fact]
    public async Task Baseline_is_silent_and_full_rescan_finds_lower_sequence_dlq_arrival_once()
    {
        var service = new Messages();
        service.Set(Dlq, 100);
        var watch = new DeliveryWatch(service);
        watch.SetTargets(7, [Dlq]);

        WatchPollResult baseline = await watch.PollAsync(100, CancellationToken.None);
        Assert.Empty(baseline.Arrivals);
        Assert.Equal(0, baseline.BaselinesPending);
        Assert.False(baseline.HasPendingScans);

        service.Set(Dlq, 20, 100);
        WatchPollResult next = await watch.PollAsync(100, CancellationToken.None);
        MessageDelivery arrival = Assert.Single(next.Arrivals);
        Assert.Equal(new DeliveryIdentity(7, Dlq.Address, Dlq.Bucket, 20), arrival.Identity);
        Assert.Empty((await watch.PollAsync(100, CancellationToken.None)).Arrivals);
    }

    [Fact]
    public async Task Same_generation_retains_existing_baselines_but_new_targets_start_silent()
    {
        var service = new Messages();
        service.Set(Queue, 1);
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue]);
        await watch.PollAsync(100, CancellationToken.None);
        service.Set(Queue, 1, 2);
        service.Set(Dlq, 8);
        watch.SetTargets(1, [Queue, Dlq]);

        WatchPollResult result = await watch.PollAsync(100, CancellationToken.None);
        Assert.Equal(new DeliveryIdentity(1, Queue.Address, Queue.Bucket, 2), Assert.Single(result.Arrivals).Identity);
        Assert.Equal(0, result.BaselinesPending);
    }

    [Fact]
    public async Task Reconnect_and_removed_then_readded_target_establish_fresh_baselines()
    {
        var service = new Messages();
        service.Set(Queue, 1);
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue]);
        await watch.PollAsync(100, CancellationToken.None);
        service.Set(Queue, 1, 2);
        watch.SetTargets(2, [Queue]);
        Assert.Empty((await watch.PollAsync(100, CancellationToken.None)).Arrivals);
        watch.SetTargets(2, []);
        Assert.Empty((await watch.PollAsync(100, CancellationToken.None)).Arrivals);
        service.Set(Queue, 1, 2, 3);
        watch.SetTargets(2, [Queue]);
        Assert.Empty((await watch.PollAsync(100, CancellationToken.None)).Arrivals);
    }

    [Fact]
    public async Task Delivery_budget_resumes_partial_baselines_and_does_not_starve_second_source()
    {
        var service = new Messages();
        service.Set(Queue, 1, 2, 3, 4, 5, 6);
        service.Set(Dlq, 10, 11, 12);
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue, Dlq]);

        WatchPollResult first = await watch.PollAsync(1, CancellationToken.None);
        Assert.Equal(1, first.ScannedDeliveries);
        Assert.True(first.HasPendingScans);
        Assert.True(first.BaselinesPending > 0);
        WatchPollResult second = await watch.PollAsync(1, CancellationToken.None);
        Assert.InRange(second.ScannedDeliveries, 0, 1);
        Assert.Contains(service.Calls, call => call.Target == Queue);
        Assert.Contains(service.Calls, call => call.Target == Dlq);
        Assert.Empty(first.Arrivals);
        Assert.Empty(second.Arrivals);

        WatchPollResult result = second;
        int scanned = first.ScannedDeliveries + second.ScannedDeliveries;
        for (int poll = 0; result.HasPendingScans && poll < 20; poll++)
        {
            result = await watch.PollAsync(1, CancellationToken.None);
            Assert.InRange(result.ScannedDeliveries, 0, 1);
            Assert.Empty(result.Arrivals);
            scanned += result.ScannedDeliveries;
        }
        Assert.False(result.HasPendingScans);
        Assert.Equal(0, result.BaselinesPending);
        Assert.Equal(9, scanned);
    }

    [Fact]
    public async Task Cancellation_rolls_back_observed_arrivals_and_cursor_for_the_entire_poll()
    {
        var service = new Messages();
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue]);
        await watch.PollAsync(100, CancellationToken.None);
        service.Set(Queue, 1, 2);
        service.PageLimit = 1;
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        service.BeforeRead = (_, _) =>
        {
            if (++calls == 2) cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch.PollAsync(100, cancellation.Token));
        service.BeforeRead = null;
        WatchPollResult retry = await watch.PollAsync(100, CancellationToken.None);
        Assert.Equal(new long[] { 1, 2 }, retry.Arrivals.Select(row => row.Identity.SequenceNumber).Order());
    }

    [Fact]
    public async Task Generation_change_rejects_late_results_even_when_service_ignores_cancellation()
    {
        var service = new Messages();
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue]);
        await watch.PollAsync(100, CancellationToken.None);
        service.Set(Queue, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeRead = async (_, _) => { started.TrySetResult(); await release.Task; };
        service.IgnoreCancellation = true;

        Task<WatchPollResult> pending = watch.PollAsync(100, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        watch.SetTargets(2, [Queue]);
        service.BeforeRead = null;
        Assert.Empty((await watch.PollAsync(100, CancellationToken.None)).Arrivals);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty((await watch.PollAsync(100, CancellationToken.None)).Arrivals);
    }

    [Fact]
    public async Task Failed_source_does_not_block_other_sources_and_can_retry_its_baseline()
    {
        var service = new Messages();
        service.Set(Queue, 1);
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue, Dlq]);
        await watch.PollAsync(100, CancellationToken.None);
        service.Set(Dlq, 3);
        service.FailingTarget = Queue;

        WatchPollResult result = await watch.PollAsync(100, CancellationToken.None);
        Assert.Equal(Dlq.Address, Assert.Single(result.Arrivals).Identity.Source);
        Assert.Equal(Queue, Assert.Single(result.Failures).Target);
        Assert.DoesNotContain("private-secret", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        service.FailingTarget = null;
        service.Set(Queue, 1, 2);
        WatchPollResult retry = await watch.PollAsync(100, CancellationToken.None);
        Assert.Contains(retry.Arrivals, row => row.Identity.Source == Queue.Address && row.Identity.SequenceNumber == 2);
        Assert.Empty(retry.Failures);
    }

    [Fact]
    public async Task Overlapping_polls_are_rejected_without_disturbing_running_poll()
    {
        var service = new Messages();
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeRead = async (_, _) => { started.TrySetResult(); await release.Task; };
        Task<WatchPollResult> pending = watch.PollAsync(100, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => watch.PollAsync(100, CancellationToken.None));
        }
        finally { release.TrySetResult(); }
        Assert.Empty((await pending).Arrivals);
    }

    [Fact]
    public async Task Large_baselines_use_broker_sized_pages_and_scan_past_short_pages()
    {
        var service = new Messages { PageLimit = 37 };
        service.Set(Queue, Enumerable.Range(1, 205).Select(value => (long)value).ToArray());
        var watch = new DeliveryWatch(service);
        watch.SetTargets(1, [Queue]);
        var result = await watch.PollAsync(300, CancellationToken.None);
        Assert.Equal(205, result.ScannedDeliveries);
        Assert.Empty(result.Arrivals);
        Assert.False(result.HasPendingScans);
        Assert.Equal(0, result.BaselinesPending);
        Assert.All(service.RequestedSizes, size => Assert.InRange(size, 1, 100));
        Assert.Equal(7, service.Calls.Count);
    }

    private sealed class Messages : IServiceBusMessageService
    {
        private readonly Dictionary<WatchTarget, ExplorerMessage[]> messages = [];
        public List<(WatchTarget Target, long? From)> Calls { get; } = [];
        public List<int> RequestedSizes { get; } = [];
        public Func<WatchTarget, CancellationToken, Task>? BeforeRead { get; set; }
        public WatchTarget? FailingTarget { get; set; }
        public int PageLimit { get; set; } = 100;
        public bool IgnoreCancellation { get; set; }

        public void Set(WatchTarget target, params long[] sequences) => messages[target] = sequences.Order()
            .Select(sequence => new ExplorerMessage($"message-{sequence}", sequence, "body", "body", 4,
                null, null, 0, null, null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>()))
            .ToArray();

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            var target = new WatchTarget(address, bucket);
            Calls.Add((target, fromSequenceNumber));
            RequestedSizes.Add(take);
            if (BeforeRead is not null) await BeforeRead(target, cancellationToken);
            if (!IgnoreCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (FailingTarget == target) throw new InvalidOperationException("private-secret");
            return messages.GetValueOrDefault(target, []).Where(row => fromSequenceNumber is null || row.SequenceNumber >= fromSequenceNumber)
                .Take(Math.Min(take, PageLimit)).ToArray();
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Watch must never send messages.");
    }
}
