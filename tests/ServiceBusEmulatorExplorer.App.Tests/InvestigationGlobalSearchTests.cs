using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationGlobalSearchTests
{
    [Fact]
    public async Task Start_searches_all_receiving_sources_and_projects_match_counts()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityAddress subscription = new(EntityKind.Subscription, "billing", "events");
        EntityDiscoverySnapshot snapshot = Snapshot(
            Queue("orders"),
            Topic("events"),
            Subscription("events", "billing"));
        var messages = new FakeMessages();
        messages.Set(queue, MessageBucket.Active, Message("order-1", 1));
        messages.Set(subscription, MessageBucket.DeadLetter, Message("invoice-2", 2));

        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(Session(snapshot, messages), 8);
        await workflow.StartAsync("*", defaultMessageId: true);

        Assert.True(workflow.IsActive);
        Assert.False(workflow.IsBusy);
        Assert.False(workflow.CanContinue);
        Assert.Equal(2, workflow.Messages.Count);
        Assert.Equal("2 matches · 2 scanned", workflow.CountSummary);
        Assert.Equal("1", workflow.Roots.Single(root => root.Name == "Queues").Children.Single().DisplayMessageCount);

        EntityNode events = workflow.Roots.Single(root => root.Name == "Topics").Children.Single();
        EntityNode billing = Assert.Single(events.Children);
        Assert.Equal("1", billing.DisplayDlqCount);
        Assert.Equal("—", billing.DisplayScheduledCount);
        Assert.Contains("complete", workflow.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Continue_retains_matches_and_stop_keeps_a_partial_search_resumable()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(queue, MessageBucket.Active, Message("order-1", 1), Message("order-2", 2));
        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(Session(snapshot, messages), 1);
        workflow.SetPreferences(new WorkspacePreferences { SearchDeliveryBudget = 1, SearchTimeBudgetSeconds = 30 });

        await workflow.StartAsync("*", defaultMessageId: true);
        Assert.True(workflow.CanContinue);
        Assert.Single(workflow.Messages);

        await workflow.ContinueAsync();
        Assert.True(workflow.CanContinue);
        Assert.Equal(2, workflow.Messages.Count);

        await workflow.ContinueAsync();
        Assert.False(workflow.CanContinue);
        Assert.Equal(2, workflow.Messages.Count);
        Assert.Equal(0, workflow.SelectedCount);

        workflow.SetAllChecked(true);
        Assert.Equal(2, workflow.SelectedCount);
        workflow.Stop();
    }

    [Fact]
    public async Task Clear_invalidates_a_late_discovery_and_restores_inactive_empty_state()
    {
        var browser = new BlockingBrowser();
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(new BrokerSession(null!, browser, new FakeMessages(), snapshot, null), 1);

        Task start = workflow.StartAsync("*", defaultMessageId: true);
        await browser.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        workflow.Clear();
        browser.Release.SetResult(snapshot);
        await start;

        Assert.False(workflow.IsActive);
        Assert.False(workflow.IsBusy);
        Assert.Empty(workflow.Messages);
        Assert.Empty(workflow.Roots);
        Assert.Empty(workflow.QueryText);
    }

    [Fact]
    public async Task Session_change_during_discovery_clears_busy_state_and_cancellation_reference()
    {
        var browser = new BlockingBrowser();
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(new BrokerSession(null!, browser, new FakeMessages(), snapshot, null), 1);

        Task start = workflow.StartAsync("*", defaultMessageId: true);
        await browser.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        workflow.SetSession(null, 2);
        await start;

        Assert.False(workflow.IsBusy);
        Assert.False(workflow.IsActive);
        workflow.Stop();
    }

    [Fact]
    public async Task Scope_filters_messages_but_keeps_the_complete_match_tree_and_selection_count_live()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityAddress subscription = new(EntityKind.Subscription, "billing", "events");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"), Topic("events"), Subscription("events", "billing"));
        var messages = new FakeMessages();
        messages.Set(queue, MessageBucket.Active, Message("order-1", 1));
        messages.Set(subscription, MessageBucket.Active, Message("invoice-2", 2));
        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(Session(snapshot, messages), 1);

        await workflow.StartAsync("*", defaultMessageId: true);
        EntityNode queueNode = workflow.Roots.Single(root => root.Name == "Queues").Children.Single();
        workflow.SelectScope(queueNode);

        Assert.Single(workflow.Messages);
        Assert.Equal(2, workflow.Roots.SelectMany(root => root.Children).Count());
        workflow.Messages[0].IsSelected = true;
        Assert.Equal(1, workflow.SelectedCount);
        workflow.SelectScope(null);
        Assert.Equal(2, workflow.Messages.Count);
        Assert.True(workflow.Messages.Single(row => row.MessageId == "order-1").IsSelected);
    }

    [Fact]
    public async Task Incomplete_discovery_cannot_claim_complete_or_offer_endless_continue()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(false, Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(queue, MessageBucket.Active, Message("order-1", 1));
        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(Session(snapshot, messages), 1);

        await workflow.StartAsync("*", defaultMessageId: true);

        Assert.False(workflow.CanContinue);
        Assert.Contains("incomplete", workflow.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start a new search", workflow.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discovery_delay_uses_the_total_budget_and_reports_a_time_limit_pause()
    {
        var workflow = new MessageSearchWorkflow();
        workflow.SetPreferences(new WorkspacePreferences { SearchTimeBudgetSeconds = 1, SearchDeliveryBudget = 10_000 });
        workflow.SetSession(new BrokerSession(
            null!,
            new DelayedBrowser(),
            new FakeMessages(),
            Snapshot(Queue("orders")),
            null), 1);

        await workflow.StartAsync("*", defaultMessageId: true);

        Assert.False(workflow.IsComplete);
        Assert.Contains("time limit", workflow.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Partial results", workflow.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_failure_retains_prior_matches_and_continue_eventually_completes()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(queue, MessageBucket.Active, Message("order-1", 1));
        var workflow = new MessageSearchWorkflow();
        workflow.SetSession(Session(snapshot, messages), 1);
        workflow.SetPreferences(new WorkspacePreferences { SearchDeliveryBudget = 1, SearchTimeBudgetSeconds = 30 });

        await workflow.StartAsync("*", defaultMessageId: true);
        messages.FailuresRemaining = 1;
        await workflow.ContinueAsync();

        Assert.Single(workflow.Messages);
        Assert.True(workflow.CanContinue);
        Assert.Contains("Partial results", workflow.Status, StringComparison.Ordinal);

        await workflow.ContinueAsync();
        Assert.True(workflow.IsComplete);
        Assert.False(workflow.CanContinue);
        Assert.Contains("complete", workflow.Status, StringComparison.OrdinalIgnoreCase);
    }

    private static BrokerSession Session(EntityDiscoverySnapshot snapshot, FakeMessages messages) =>
        new(null!, new FakeBrowser(snapshot), messages, snapshot, null);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities) =>
        Snapshot(true, entities);

    private static EntityDiscoverySnapshot Snapshot(bool complete, params ServiceBusEntityNode[] entities) =>
        new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            complete,
            []);

    private static ServiceBusEntityNode Queue(string name) =>
        new(EntityKind.Queue, name, null, new EntityRuntimeCounts(0, 0, 0, 0), Metadata(name));

    private static ServiceBusEntityNode Topic(string name) =>
        new(EntityKind.Topic, name, null, new EntityRuntimeCounts(0, 0, 0, 0), Metadata(name));

    private static ServiceBusEntityNode Subscription(string topic, string name) =>
        new(EntityKind.Subscription, name, topic, new EntityRuntimeCounts(0, 0, 0, 0), Metadata($"{topic}/subscriptions/{name}"));

    private static EntityMetadata Metadata(string path) =>
        new(path, "Active", null, null, null, null, null, null, null);

    private static ExplorerMessage Message(string id, long sequence) => new(
        id, sequence, id, id, id.Length, null, null, 0, null, null, null, null,
        new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class BlockingBrowser : IInvestigationEntityBrowser
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EntityDiscoverySnapshot> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            Started.SetResult(true);
            return await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DelayedBrowser : IInvestigationEntityBrowser
    {
        public async Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The discovery delay should have been canceled.");
        }
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> messages = [];

        public int FailuresRemaining { get; set; }

        public void Set(EntityAddress address, MessageBucket bucket, params ExplorerMessage[] values) =>
            messages[(address, bucket)] = values;

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("simulated source failure");
            }

            IReadOnlyList<ExplorerMessage> values = messages.TryGetValue((address, bucket), out IReadOnlyList<ExplorerMessage>? found)
                ? found
                : [];
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(values
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToList());
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
