using System.IO;
using System.Threading.Channels;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationWatchNotificationLifecycleTests
{
    private static readonly WatchPreference[] Rules = [new(WatchScopeResolver.ConnectionScopeKey, true, false)];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disconnected_credential_edit_clears_pending_only_after_successful_save(bool saveSucceeds)
    {
        var original = new WorkspacePreferences();
        var store = new PreferencesStore(original) { FailSave = !saveSucceeds };
        var workspace = new InvestigationWorkspace(store, new BrokerConnectionWorkflow(
            () => throw new InvalidOperationException("Offline settings must not connect."),
            _ => throw new InvalidOperationException("Offline settings must not discover."),
            _ => throw new InvalidOperationException("Offline settings must not read messages.")));
        await workspace.InitializeAsync();
        var message = new ExplorerMessage("pending", 1, "{}", "{}", 2, null, null, 0,
            null, null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());
        var pending = new MessageDelivery(new DeliveryIdentity(1,
            new EntityAddress(EntityKind.Queue, "orders"), MessageBucket.Active, 1), message);
        workspace.Watch.PendingArrivals.Add(pending);
        var profile = original.Profiles.Single();
        var replacement = original with
        {
            Profiles = [profile with { Connection = profile.Connection with
                { RuntimeConnectionString = "Endpoint=sb://replacement.example/;SharedAccessKeyName=test;SharedAccessKey=test" } }]
        };
        try
        {
            Assert.False(workspace.IsConnected);
            if (saveSucceeds)
            {
                await workspace.ApplyPreferencesAsync(replacement);
                Assert.Empty(workspace.Watch.PendingArrivals);
                Assert.Equal(replacement.Profiles.Single().Connection, workspace.Preferences.Profiles.Single().Connection);
                Assert.Equal(replacement.Profiles.Single().Connection, store.Saved!.Profiles.Single().Connection);
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => workspace.ApplyPreferencesAsync(replacement));
                Assert.Same(pending, Assert.Single(workspace.Watch.PendingArrivals));
                Assert.Equal(profile.Connection, workspace.Preferences.Profiles.Single().Connection);
                Assert.Null(store.Saved);
            }
        }
        finally
        {
            store.FailSave = false;
            await workspace.DisposeAsync();
        }
    }

    [Fact]
    public async Task Discovery_event_exposes_partial_observations_while_missing_known_sources_remain_watched()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        var workflow = new MessageWatchWorkflow(clock);
        var polls = Observe(workflow);
        var discoveries = Channel.CreateUnbounded<EntityDiscoverySnapshot>();
        workflow.DiscoveryUpdated += snapshot => discoveries.Writer.TryWrite(snapshot);
        try
        {
            workflow.Start(Session(browser, messages), 1, Rules);
            await Next(polls);
            await clock.Ready();
            Assert.Single((await discoveries.Reader.ReadAsync()).Entities);

            var partial = Snapshot(false);
            browser.Current = partial;
            messages.Sequences = [1, 2];
            clock.Fire();
            var poll = await Next(polls);
            await clock.Ready();

            var observed = await discoveries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(partial, observed);
            Assert.Empty(observed.Entities);
            Assert.False(observed.IsComplete);
            Assert.Equal(2, Assert.Single(poll.Arrivals).Identity.SequenceNumber);
            Assert.Equal("orders", Assert.Single(workflow.PendingArrivals).Identity.Source.Name);
        }
        finally { await workflow.StopAsync(); }
    }

    [Fact]
    public async Task Retained_notifications_survive_disconnect_and_reconnect_uses_a_fresh_silent_baseline()
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        var workflow = new MessageWatchWorkflow(clock);
        var polls = Observe(workflow);
        try
        {
            workflow.Start(Session(browser, messages), 1, Rules);
            await Next(polls);
            await clock.Ready();
            messages.Sequences = [1, 2];
            clock.Fire();
            await Next(polls);
            await clock.Ready();
            var retained = Assert.Single(workflow.PendingArrivals);

            await workflow.StopAsync(clearPending: false);
            Assert.Null(workflow.ConnectionGeneration);
            Assert.Same(retained, Assert.Single(workflow.PendingArrivals));

            messages.Sequences = [1, 2, 3];
            workflow.Start(Session(browser, messages), 2, Rules);
            Assert.Empty((await Next(polls)).Arrivals);
            await clock.Ready();
            Assert.Same(retained, Assert.Single(workflow.PendingArrivals));

            messages.Sequences = [1, 2, 3, 4];
            clock.Fire();
            var arrival = Assert.Single((await Next(polls)).Arrivals);
            await clock.Ready();
            Assert.Equal(2, arrival.Identity.ConnectionGeneration);
            Assert.Equal(4, arrival.Identity.SequenceNumber);
            Assert.Equal(2, workflow.PendingArrivals.Count);
        }
        finally { await workflow.StopAsync(); }
        Assert.Empty(workflow.PendingArrivals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disconnected_pending_notifications_can_be_cleared_or_pruned_by_disabled_rules(bool clearByStop)
    {
        var clock = new PollClock();
        var browser = new Browser(Snapshot(true, "orders"));
        var messages = new Messages();
        var workflow = new MessageWatchWorkflow(clock);
        var polls = Observe(workflow);
        try
        {
            workflow.Start(Session(browser, messages), 1, Rules);
            await Next(polls);
            await clock.Ready();
            messages.Sequences = [1, 2];
            clock.Fire();
            await Next(polls);
            await clock.Ready();
            await workflow.StopAsync(clearPending: false);
            Assert.Single(workflow.PendingArrivals);

            if (clearByStop) await workflow.StopAsync();
            else workflow.UpdateRules([new(WatchScopeResolver.ConnectionScopeKey, false, false)]);

            Assert.Empty(workflow.PendingArrivals);
        }
        finally { await workflow.StopAsync(); }
    }

    private static Channel<WatchPollResult> Observe(MessageWatchWorkflow workflow)
    {
        var polls = Channel.CreateUnbounded<WatchPollResult>();
        workflow.Polled += result => polls.Writer.TryWrite(result);
        return polls;
    }

    private static async Task<WatchPollResult> Next(Channel<WatchPollResult> polls) =>
        await polls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

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
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
    }

    private sealed class PreferencesStore(WorkspacePreferences original) : IWorkspacePreferencesStore
    {
        public bool FailSave { get; set; }
        public WorkspacePreferences? Saved { get; private set; }
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(original));
        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            if (FailSave) throw new IOException("Test persistence failure.");
            Saved = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public long[] Sequences { get; set; } = [1];
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExplorerMessage>>(Sequences
                .Where(sequence => sequence >= (fromSequenceNumber ?? 0)).Take(take)
                .Select(sequence => new ExplorerMessage($"message-{sequence}", sequence, "{}", "{}", 2,
                    null, null, 0, null, null, null, null, new Dictionary<string, object?>(),
                    new Dictionary<string, object?>())).ToArray());
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Watch must never send messages.");
    }

    private sealed class PollClock : TimeProvider
    {
        private readonly Channel<PollTimer> delays = Channel.CreateUnbounded<PollTimer>();
        private PollTimer? pending;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PollTimer(callback, state);
            if (dueTime == TimeSpan.FromSeconds(15)) delays.Writer.TryWrite(timer);
            return timer;
        }

        public async Task Ready() => pending = await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public void Fire() { var timer = pending!; pending = null; timer.Fire(); }

        private sealed class PollTimer(TimerCallback callback, object? state) : ITimer
        {
            private int disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Fire() { if (Interlocked.Exchange(ref disposed, 1) == 0) callback(state); }
            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
