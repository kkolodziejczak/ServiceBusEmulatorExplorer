using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class BackgroundMessageRefreshTests
{
    [Fact]
    public async Task Refresh_updates_watched_queue_while_another_entity_is_selected_and_reuses_cache()
    {
        EntityAddress watchedAddress = QueueAddress("watched");
        EntityAddress selectedAddress = QueueAddress("selected");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("watched"), Queue("selected"));
        var messages = new FakeMessages();
        messages.Set(watchedAddress, Enumerable.Range(1, 51).Select(value => (long)value).ToArray());
        messages.Set(selectedAddress, 100);
        var workflow = Browse(snapshot, messages);
        workflow.SetWatchRules([new(WatchScopeResolver.QueueScopeKey("watched"), true, false)]);
        EntityNode selected = workflow.AllEntities().Single(node => node.Address == selectedAddress);

        await workflow.SelectAsync(selected, deadLetter: false);
        int watchedPeeksBeforeBackground = messages.CallsFor(watchedAddress, MessageBucket.Active);
        await workflow.RefreshBackgroundAsync(refreshSelected: false);

        Assert.True(messages.CallsFor(watchedAddress, MessageBucket.Active) > watchedPeeksBeforeBackground);
        Assert.Equal(selectedAddress, workflow.SelectedEntity?.Address);
        Assert.Equal(100, Assert.Single(workflow.Messages).Key.SequenceNumber);

        EntityNode watched = workflow.AllEntities().Single(node => node.Address == watchedAddress);
        int peeksBeforeReturn = messages.CallsFor(watchedAddress, MessageBucket.Active);
        await workflow.SelectAsync(watched, deadLetter: false);

        Assert.Equal(Enumerable.Range(1, 50).Select(value => (long)value),
            workflow.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Equal(peeksBeforeReturn, messages.CallsFor(watchedAddress, MessageBucket.Active));

        await workflow.LoadMoreAsync();
        Assert.Equal(Enumerable.Range(1, 51).Select(value => (long)value),
            workflow.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Equal(peeksBeforeReturn + 2, messages.CallsFor(watchedAddress, MessageBucket.Active));
        Assert.Equal(new long?[] { 51, 52 }, messages.Requests
            .Where(request => request.Address == watchedAddress && request.Bucket == MessageBucket.Active)
            .TakeLast(2)
            .Select(request => request.FromSequenceNumber));
    }

    [Fact]
    public async Task Background_refresh_preserves_loaded_depth_and_continuation_after_returning_to_cached_scope()
    {
        EntityAddress watchedAddress = QueueAddress("watched");
        EntityAddress otherAddress = QueueAddress("other");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("watched"), Queue("other"));
        var messages = new FakeMessages();
        messages.Set(watchedAddress, Enumerable.Range(1, 60).Select(value => (long)value).ToArray());
        messages.Set(otherAddress, 1000);
        var workflow = Browse(snapshot, messages);
        workflow.SetWatchRules([new(WatchScopeResolver.QueueScopeKey("watched"), true, false)]);
        EntityNode watched = workflow.AllEntities().Single(node => node.Address == watchedAddress);
        EntityNode other = workflow.AllEntities().Single(node => node.Address == otherAddress);

        await workflow.RefreshBackgroundAsync(refreshSelected: false);
        await workflow.SelectAsync(watched, deadLetter: false);
        await workflow.LoadMoreAsync();
        Assert.Equal(60, workflow.Messages.Count);
        await workflow.SelectAsync(other, deadLetter: false);

        messages.Set(watchedAddress, Enumerable.Range(2, 60).Select(value => (long)value).ToArray());
        await workflow.RefreshBackgroundAsync(refreshSelected: false);

        int readsBeforeReturn = messages.CallsFor(watchedAddress, MessageBucket.Active);
        await workflow.SelectAsync(watched, deadLetter: false);
        Assert.Equal(Enumerable.Range(2, 60).Select(value => (long)value),
            workflow.Messages.Select(row => row.Key.SequenceNumber));
        Assert.True(workflow.CanLoadMore);
        Assert.Equal(readsBeforeReturn, messages.CallsFor(watchedAddress, MessageBucket.Active));

        await workflow.LoadMoreAsync();

        Assert.Equal(60, workflow.Messages.Count);
        Assert.Equal(62, messages.Requests.Last(request => request.Address == watchedAddress).FromSequenceNumber);
        Assert.False(workflow.CanLoadMore);
    }

    [Fact]
    public async Task Background_refresh_keeps_active_and_dead_letter_watch_scopes_separate()
    {
        EntityAddress address = QueueAddress("orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, MessageBucket.Active, 1);
        messages.Set(address, MessageBucket.DeadLetter, 2);
        var workflow = Browse(snapshot, messages);
        workflow.SetWatchRules([new(WatchScopeResolver.QueueScopeKey("orders"), true, false)]);

        await workflow.RefreshBackgroundAsync(refreshSelected: false);

        Assert.True(messages.CallsFor(address, MessageBucket.Active) > 0);
        Assert.Equal(0, messages.CallsFor(address, MessageBucket.DeadLetter));
        EntityNode orders = workflow.AllEntities().Single(node => node.Address == address);
        await workflow.SelectAsync(orders, deadLetter: true);

        Assert.Equal(2, Assert.Single(workflow.Messages).Key.SequenceNumber);
        Assert.Equal(2, messages.CallsFor(address, MessageBucket.DeadLetter));
    }

    [Fact]
    public async Task Background_refresh_discovers_and_caches_new_subscription_under_watched_topic()
    {
        EntityAddress firstAddress = SubscriptionAddress("orders", "first");
        EntityAddress newAddress = SubscriptionAddress("orders", "new");
        EntityDiscoverySnapshot initial = Snapshot(Topic("orders"), Subscription("orders", "first"));
        EntityDiscoverySnapshot discovered = Snapshot(Topic("orders"), Subscription("orders", "first"), Subscription("orders", "new"));
        var browser = new FakeBrowser(discovered);
        var messages = new FakeMessages();
        messages.Set(firstAddress, 1);
        messages.Set(newAddress, 2);
        var workflow = Browse(initial, browser, messages);
        workflow.SetWatchRules([new(WatchScopeResolver.TopicScopeKey("orders"), true, false)]);

        await workflow.RefreshBackgroundAsync(refreshSelected: false);

        Assert.True(messages.CallsFor(newAddress, MessageBucket.Active) > 0);
        EntityNode newSubscription = workflow.AllEntities().Single(node => node.Address == newAddress);
        int readsBeforeSelection = messages.CallsFor(newAddress, MessageBucket.Active);
        await workflow.SelectAsync(newSubscription, deadLetter: false);

        Assert.Equal(2, Assert.Single(workflow.Messages).Key.SequenceNumber);
        Assert.Equal(readsBeforeSelection, messages.CallsFor(newAddress, MessageBucket.Active));
        Assert.Equal(1, browser.Calls);
    }

    [Fact]
    public async Task Removing_watch_rules_evicts_cached_messages()
    {
        EntityAddress address = QueueAddress("orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, 1);
        var workflow = Browse(snapshot, messages);
        workflow.SetWatchRules([new(WatchScopeResolver.QueueScopeKey("orders"), true, false)]);
        await workflow.RefreshBackgroundAsync(refreshSelected: false);
        Assert.True(workflow.HasWatchedTargets);

        workflow.SetWatchRules([]);

        Assert.False(workflow.HasWatchedTargets);
        EntityNode orders = workflow.AllEntities().Single(node => node.Address == address);
        await workflow.SelectAsync(orders, deadLetter: false);
        Assert.Equal(4, messages.CallsFor(address, MessageBucket.Active));
    }

    [Fact]
    public async Task Session_change_prevents_an_in_flight_background_read_from_becoming_current_cache()
    {
        EntityAddress address = QueueAddress("orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        messages.BlockNext(address, entered, release);
        var workflow = Browse(snapshot, messages);
        workflow.SetWatchRules([new(WatchScopeResolver.QueueScopeKey("orders"), true, false)]);

        Task refresh = workflow.RefreshBackgroundAsync(refreshSelected: false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        workflow.SetSession(null, 2);
        release.SetResult();
        await refresh;

        Assert.Null(workflow.SelectedEntity);
        Assert.Empty(workflow.Messages);

        messages.Set(address, 2);
        workflow.SetSession(new BrokerSession(null!, new FakeBrowser(snapshot), messages, snapshot, null), 3);
        EntityNode orders = workflow.AllEntities().Single(node => node.Address == address);
        await workflow.SelectAsync(orders, deadLetter: false);

        Assert.Equal(2, Assert.Single(workflow.Messages).Key.SequenceNumber);
        Assert.Equal(3, messages.CallsFor(address, MessageBucket.Active));
    }

    [Fact]
    public async Task Identical_watch_rules_after_reconnect_keep_target_active_and_use_the_new_session()
    {
        EntityAddress address = QueueAddress("orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, 1);
        var workflow = Browse(snapshot, messages);
        WatchPreference[] rules = [new(WatchScopeResolver.QueueScopeKey("orders"), true, false)];
        workflow.SetWatchRules(rules);
        await workflow.RefreshBackgroundAsync(refreshSelected: false);
        Assert.True(workflow.HasWatchedTargets);

        workflow.SetSession(null, 2);
        messages.Set(address, 2);
        workflow.SetSession(new BrokerSession(null!, new FakeBrowser(snapshot), messages, snapshot, null), 3);
        workflow.SetWatchRules(rules);

        Assert.True(workflow.HasWatchedTargets);
        EntityNode orders = workflow.AllEntities().Single(node => node.Address == address);
        await workflow.SelectAsync(orders, deadLetter: false);

        Assert.Equal(2, Assert.Single(workflow.Messages).Key.SequenceNumber);
        Assert.Equal(4, messages.CallsFor(address, MessageBucket.Active));
    }

    [Fact]
    public async Task Failure_for_one_watched_entity_does_not_block_other_watched_entities()
    {
        EntityAddress brokenAddress = QueueAddress("broken");
        EntityAddress healthyAddress = QueueAddress("healthy");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("broken"), Queue("healthy"));
        var messages = new FakeMessages { FailingAddress = brokenAddress };
        messages.Set(healthyAddress, 7);
        var workflow = Browse(snapshot, messages);
        workflow.SetWatchRules([
            new(WatchScopeResolver.QueueScopeKey("broken"), true, false),
            new(WatchScopeResolver.QueueScopeKey("healthy"), true, false)
        ]);

        await workflow.RefreshBackgroundAsync(refreshSelected: false);

        EntityNode healthy = workflow.AllEntities().Single(node => node.Address == healthyAddress);
        int healthyReadsBeforeSelection = messages.CallsFor(healthyAddress, MessageBucket.Active);
        await workflow.SelectAsync(healthy, deadLetter: false);

        Assert.Equal(7, Assert.Single(workflow.Messages).Key.SequenceNumber);
        Assert.Equal(healthyReadsBeforeSelection, messages.CallsFor(healthyAddress, MessageBucket.Active));
        Assert.Equal(1, messages.CallsFor(brokenAddress, MessageBucket.Active));
    }

    [Fact]
    public async Task Background_refresh_updates_observed_counts_for_emulator_topic_and_subscriptions()
    {
        EntityAddress firstAddress = SubscriptionAddress("events", "first");
        EntityAddress secondAddress = SubscriptionAddress("events", "second");
        EntityDiscoverySnapshot snapshot = Snapshot(Topic("events"), Subscription("events", "first"), Subscription("events", "second"));
        var messages = new FakeMessages();
        messages.Set(firstAddress, 1, 2);
        messages.Set(secondAddress, 3);
        var workflow = Browse(snapshot, new FakeBrowser(snapshot), messages, supportsRuntimeCounts: false);
        workflow.SetWatchRules([new(WatchScopeResolver.TopicScopeKey("events"), true, false)]);

        await workflow.RefreshBackgroundAsync(refreshSelected: false);

        Assert.Equal("2*", workflow.AllEntities().Single(node => node.Address == firstAddress).DisplayMessageCount);
        Assert.Equal("1*", workflow.AllEntities().Single(node => node.Address == secondAddress).DisplayMessageCount);
        EntityNode topic = workflow.AllEntities().Single(node => node.Name == "events" && node.Kind == nameof(EntityKind.Topic));
        Assert.Equal("3*", topic.DisplayMessageCount);
        Assert.Contains("scan complete", topic.ActiveCountDetail, StringComparison.Ordinal);
    }

    private static MessageBrowseWorkflow Browse(EntityDiscoverySnapshot snapshot, FakeMessages messages)
        => Browse(snapshot, new FakeBrowser(snapshot), messages);

    private static MessageBrowseWorkflow Browse(
        EntityDiscoverySnapshot snapshot,
        FakeBrowser browser,
        FakeMessages messages,
        bool supportsRuntimeCounts = true)
    {
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(new BrokerSession(new FakeFactory(supportsRuntimeCounts), browser, messages, snapshot, null), 1);
        return workflow;
    }

    private static EntityAddress QueueAddress(string name) => new(EntityKind.Queue, name);

    private static EntityAddress SubscriptionAddress(string topic, string name)
        => new(EntityKind.Subscription, name, topic);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities)
        => new(entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known))))
            .ToArray(), DateTimeOffset.UtcNow, IsComplete: true, Issues: []);

    private static ServiceBusEntityNode Queue(string name)
        => Entity(EntityKind.Queue, name, null);

    private static ServiceBusEntityNode Topic(string name)
        => Entity(EntityKind.Topic, name, null);

    private static ServiceBusEntityNode Subscription(string topic, string name)
        => Entity(EntityKind.Subscription, name, topic);

    private static ServiceBusEntityNode Entity(EntityKind kind, string name, string? topic)
        => new(kind, name, topic, new(0, 0, 0, 0), new(name, "Active", null, null, null, null, null, null, null));

    private static ExplorerMessage Message(long sequence)
        => new($"message-{sequence}", sequence, $"body-{sequence}", $"body-{sequence}", 7,
            null, null, 0, null, null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private sealed class FakeBrowser(params EntityDiscoverySnapshot[] snapshots) : IInvestigationEntityBrowser
    {
        private int index;
        public int Calls { get; private set; }

        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            Calls++;
            int current = Math.Min(index++, snapshots.Length - 1);
            return Task.FromResult(snapshots[current]);
        }
    }

    private sealed class FakeFactory(bool supportsRuntimeCounts) : IServiceBusClientFactory
    {
        public bool SupportsRuntimeCounts => supportsRuntimeCounts;
        public ServiceBusAdministrationClient AdministrationClient => null!;
        public ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> messages = [];
        private EntityAddress? blockedAddress;
        private TaskCompletionSource? blockedStarted;
        private TaskCompletionSource? releaseBlocked;

        public List<PeekRequest> Requests { get; } = [];
        public EntityAddress? FailingAddress { get; init; }

        public void Set(EntityAddress address, params long[] sequences)
            => Set(address, MessageBucket.Active, sequences);

        public void Set(EntityAddress address, MessageBucket bucket, params long[] sequences)
            => messages[(address, bucket)] = sequences.Select(Message).ToArray();

        public int CallsFor(EntityAddress address, MessageBucket bucket)
            => Requests.Count(request => request.Address == address && request.Bucket == bucket);

        public void BlockNext(EntityAddress address, TaskCompletionSource started, TaskCompletionSource release)
        {
            blockedAddress = address;
            blockedStarted = started;
            releaseBlocked = release;
        }

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(address, bucket, take, fromSequenceNumber));
            if (address == blockedAddress)
            {
                blockedAddress = null;
                blockedStarted!.TrySetResult();
                // Deliberately ignore cancellation to prove stale results are rejected on completion.
                await releaseBlocked!.Task;
            }
            if (address == FailingAddress) throw new InvalidOperationException("Synthetic target failure.");
            return messages.TryGetValue((address, bucket), out IReadOnlyList<ExplorerMessage>? source)
                ? source.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                    .Take(take).ToArray()
                : [];
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed record PeekRequest(EntityAddress Address, MessageBucket Bucket, int Take, long? FromSequenceNumber);
}
