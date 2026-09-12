using System.Threading.Channels;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationWatchWorkflowTests
{
    private static readonly WatchPreference[] WatchActive = [new(WatchScopeResolver.ConnectionScopeKey, true, false)];

    [Fact]
    public async Task Starts_immediately_then_polls_after_fifteen_seconds_with_a_silent_baseline()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        messages.Set("orders", 1);
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        try
        {
            workflow.Start(Session(browser, messages), 7, WatchActive);
            Assert.Empty((await Next(results)).Arrivals);
            await clock.WaitForPollDelay();
            Assert.Equal(1, browser.Calls);

            messages.Set("orders", 1, 2);
            clock.FirePollDelay();
            var arrival = Assert.Single((await Next(results)).Arrivals);
            Assert.Equal(2, arrival.Identity.SequenceNumber);
            Assert.Equal(2, browser.Calls);
            Assert.Equal(arrival.Identity, Assert.Single(workflow.PendingArrivals).Identity);

            await clock.WaitForPollDelay();
            clock.FirePollDelay();
            Assert.Empty((await Next(results)).Arrivals);
            Assert.Single(workflow.PendingArrivals);
        }
        finally { await workflow.StopAsync(); }
        Assert.Empty(workflow.PendingArrivals);
    }

    [Fact]
    public async Task Disabled_rules_make_no_discovery_or_message_requests()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        var workflow = new MessageWatchWorkflow(clock);
        workflow.Start(Session(browser, messages), 1, []);
        await clock.WaitForPollDelay();
        clock.FirePollDelay();
        await clock.WaitForPollDelay();
        await workflow.StopAsync();
        Assert.Equal(0, browser.Calls);
        Assert.Equal(0, messages.Calls);
    }

    [Fact]
    public async Task Incomplete_discovery_retains_missing_sources_but_complete_removal_resets_their_baseline()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        messages.Set("orders", 1);
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        try
        {
            workflow.Start(Session(browser, messages), 1, WatchActive);
            await Next(results);
            await clock.WaitForPollDelay();
            browser.Current = Snapshot(false);
            messages.Set("orders", 1, 2);
            clock.FirePollDelay();
            Assert.Equal(2, Assert.Single((await Next(results)).Arrivals).Identity.SequenceNumber);

            await clock.WaitForPollDelay();
            browser.Current = Snapshot(true);
            clock.FirePollDelay();
            await clock.WaitForPollDelay();
            while (results.Reader.TryRead(out _)) { }
            browser.Current = Snapshot(true, "orders");
            messages.Set("orders", 1, 2, 3);
            clock.FirePollDelay();
            Assert.Empty((await Next(results)).Arrivals);
        }
        finally { await workflow.StopAsync(); }
    }

    [Fact]
    public async Task New_sources_discovered_under_global_rules_start_with_their_own_silent_baseline()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        messages.Set("orders", 1);
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        try
        {
            workflow.Start(Session(browser, messages), 1, WatchActive);
            await Next(results);
            await clock.WaitForPollDelay();
            browser.Current = Snapshot(true, "orders", "new-queue");
            messages.Set("orders", 1, 2);
            messages.Set("new-queue", 90);
            clock.FirePollDelay();
            Assert.Equal(2, Assert.Single((await Next(results)).Arrivals).Identity.SequenceNumber);

            await clock.WaitForPollDelay();
            messages.Set("new-queue", 90, 91);
            clock.FirePollDelay();
            Assert.Equal(91, Assert.Single((await Next(results)).Arrivals).Identity.SequenceNumber);
        }
        finally { await workflow.StopAsync(); }
    }

    [Fact]
    public async Task Rule_changes_preserve_unchanged_sources_and_baseline_new_targets()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders", "billing"));
        var messages = new Messages();
        messages.Set("orders", 1);
        messages.Set("billing", 90);
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        try
        {
            workflow.Start(Session(browser, messages), 1,
                [new(WatchScopeResolver.QueueScopeKey("orders"), true, false)]);
            await Next(results);
            await clock.WaitForPollDelay();
            messages.Set("orders", 1, 2);
            workflow.UpdateRules(WatchActive);
            // A rule change wakes the scheduler; it must not replace the engine and lose its baseline.
            Assert.Equal(2, Assert.Single((await Next(results)).Arrivals).Identity.SequenceNumber);
        }
        finally { await workflow.StopAsync(); }
    }

    [Fact]
    public async Task Stop_cancels_discovery_and_suppresses_late_results_and_warnings()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<EntityDiscoverySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Discover = token => { entered.TrySetResult(token); return release.Task; };
        var messages = new Messages();
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        var warnings = Channel.CreateUnbounded<string>();
        workflow.Warning += warning => warnings.Writer.TryWrite(warning);
        workflow.Start(Session(browser, messages), 1, WatchActive);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = workflow.StopAsync();
        Assert.True(token.IsCancellationRequested);
        release.SetResult(browser.Current);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(results.Reader.TryRead(out _));
        Assert.False(warnings.Reader.TryRead(out _));
        Assert.Equal(0, messages.Calls);
    }

    [Fact]
    public async Task Discovery_timeout_warns_and_next_poll_recovers()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Discover = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return browser.Current;
        };
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        var warnings = Channel.CreateUnbounded<string>();
        workflow.Warning += warning => warnings.Writer.TryWrite(warning);
        try
        {
            workflow.Start(Session(browser, new Messages()), 1, WatchActive);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireDelay(TimeSpan.FromSeconds(30));
            var warning = await warnings.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("timed out", warning, StringComparison.OrdinalIgnoreCase);
            Assert.False(results.Reader.TryRead(out _));
            await clock.WaitForPollDelay();
            browser.Discover = null;
            clock.FirePollDelay();
            Assert.Empty((await Next(results)).Arrivals);
            Assert.Equal(2, browser.Calls);
        }
        finally { await workflow.StopAsync(); }
    }

    [Fact]
    public async Task Timeout_after_a_committed_page_retains_arrivals_and_resumes_the_unfinished_scan()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        messages.Set("orders", 1);
        var workflow = new MessageWatchWorkflow(clock);
        var results = Observe(workflow);
        var warnings = Channel.CreateUnbounded<string>();
        workflow.Warning += warning => warnings.Writer.TryWrite(warning);
        try
        {
            workflow.Start(Session(browser, messages), 1, WatchActive);
            await Next(results);
            await clock.WaitForPollDelay();
            messages.Set("orders", Enumerable.Range(1, 150).Select(value => (long)value).ToArray());
            messages.BlockFromSequence = 101;
            clock.FirePollDelay();
            Assert.Equal(99, (await Next(results)).Arrivals.Count);
            await messages.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireDelay(TimeSpan.FromSeconds(30));
            await warnings.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(99, workflow.PendingArrivals.Count);

            await clock.WaitForPollDelay();
            messages.BlockFromSequence = null;
            clock.FirePollDelay();
            var remaining = await Next(results);
            Assert.Equal(50, remaining.Arrivals.Count);
            Assert.All(remaining.Arrivals, arrival => Assert.InRange(arrival.Identity.SequenceNumber, 101, 150));
            Assert.Equal(149, workflow.PendingArrivals.Count);
        }
        finally { await workflow.StopAsync(); }
    }

    private static Channel<WatchPollResult> Observe(MessageWatchWorkflow workflow)
    {
        var channel = Channel.CreateUnbounded<WatchPollResult>();
        workflow.Polled += result => channel.Writer.TryWrite(result);
        return channel;
    }

    private static async Task<WatchPollResult> Next(Channel<WatchPollResult> results) =>
        await results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private static BrokerSession Session(Browser browser, Messages messages) =>
        new(null!, browser, messages, browser.Current, null);

    private static EntityDiscoverySnapshot Snapshot(bool complete, params string[] names) => new(
        names.Select(name => new EntityObservation(new DiscoveredEntity(EntityKind.Queue, name, null,
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
            new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known),
                new(0, CountAvailability.Known)))).ToArray(), DateTimeOffset.UtcNow, complete, []);

    private sealed class Browser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public EntityDiscoverySnapshot Current { get; set; } = snapshot;
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<EntityDiscoverySnapshot>>? Discover { get; set; }
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Discover?.Invoke(cancellationToken) ?? Task.FromResult(Current);
        }
    }

    private sealed class Messages : IServiceBusMessageService
    {
        private readonly Dictionary<string, long[]> values = [];
        public int Calls { get; private set; }
        public long? BlockFromSequence { get; set; }
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Set(string queue, params long[] sequences) => values[queue] = sequences;
        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            Calls++;
            if (BlockFromSequence is long blockedFrom && fromSequenceNumber >= blockedFrom)
            {
                Blocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return values.GetValueOrDefault(address.Name, [])
                .Where(sequence => sequence >= (fromSequenceNumber ?? 0)).Take(take)
                .Select(sequence => new ExplorerMessage($"message-{sequence}", sequence, "{}", "{}", 2,
                    null, null, 0, null, null, null, null, new Dictionary<string, object?>(),
                    new Dictionary<string, object?>())).ToArray();
        }
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Watch must never send messages.");
    }

    // Only advances the scheduled 15-second delay. Timeout timers are deliberately left pending.
    private sealed class PollClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<Timer> timers = [];
        private readonly SemaphoreSlim changed = new(0);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, dueTime, period);
            lock (gate) timers.Add(timer);
            changed.Release();
            return timer;
        }

        public async Task WaitForPollDelay()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                lock (gate)
                    if (timers.Any(timer => timer.IsPollDelay)) return;
                await changed.WaitAsync(timeout.Token);
            }
        }

        public void FirePollDelay() => FireDelay(TimeSpan.FromSeconds(15));

        public void FireDelay(TimeSpan delay)
        {
            Timer timer;
            lock (gate) timer = timers.First(timer => timer.IsDelay(delay));
            timer.Fire();
        }

        private sealed class Timer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
        {
            private int disposed;
            private TimeSpan due = dueTime;
            public bool IsPollDelay => IsDelay(TimeSpan.FromSeconds(15));
            public bool IsDelay(TimeSpan delay) => Volatile.Read(ref disposed) == 0 && due == delay;
            public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
            {
                due = newDueTime;
                return Volatile.Read(ref disposed) == 0;
            }
            public void Fire()
            {
                if (period == Timeout.InfiniteTimeSpan) Interlocked.Exchange(ref disposed, 1);
                callback(state);
            }
            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
