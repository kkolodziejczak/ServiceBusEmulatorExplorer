using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationBrowseTests
{
    [Fact]
    public void SetSession_exposes_connection_state_and_notifies_bindings()
    {
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var workflow = new MessageBrowseWorkflow();
        var changes = new List<string?>();
        workflow.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        workflow.SetSession(Session(new FakeBrowser(snapshot), new FakeMessages(), snapshot), 1);

        Assert.True(workflow.IsConnected);
        Assert.Contains(nameof(MessageBrowseWorkflow.IsConnected), changes);

        changes.Clear();
        workflow.SetSession(null, 2);

        Assert.False(workflow.IsConnected);
        Assert.Contains(nameof(MessageBrowseWorkflow.IsConnected), changes);
    }

    [Fact]
    public async Task Refresh_preserves_checked_and_focused_delivery_references_by_identity()
    {
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var browser = new FakeBrowser(snapshot, snapshot);
        var messages = new FakeMessages();
        messages.Set(new EntityAddress(EntityKind.Queue, "orders"), Message(1), Message(2));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(browser, messages, snapshot), 1);
        EntityNode queue = workflow.AllEntities().Single(node => node.Name == "orders");
        await workflow.SelectAsync(queue, deadLetter: false);

        MessageRow selected = workflow.Messages.Single(row => row.Key.SequenceNumber == 1);
        MessageRow focused = workflow.Messages.Single(row => row.Key.SequenceNumber == 2);
        selected.IsSelected = true;
        workflow.FocusedMessage = focused;

        await workflow.RefreshAsync();

        Assert.Same(selected, workflow.Messages.Single(row => row.Key.SequenceNumber == 1));
        Assert.True(selected.IsSelected);
        Assert.Same(focused, workflow.Messages.Single(row => row.Key.SequenceNumber == 2));
        Assert.Equal(focused.Key, workflow.FocusedMessage?.Key);
        Assert.Same(focused, workflow.FocusedMessage);
    }

    [Fact]
    public async Task Refresh_reconciles_entity_nodes_and_preserves_expansion_filter_and_selection()
    {
        EntityDiscoverySnapshot initial = Snapshot(Queue("orders", 4), Queue("audit", 2));
        EntityDiscoverySnapshot refreshed = Snapshot(Queue("orders", 12), Queue("audit", 5));
        var browser = new FakeBrowser(refreshed);
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(browser, new FakeMessages(), initial), 1);
        EntityNode orders = workflow.AllEntities().Single(node => node.Name == "orders");
        orders.IsExpanded = false;
        workflow.FilterEntities("orders");
        await workflow.SelectAsync(orders, deadLetter: false);

        await workflow.RefreshAsync();

        EntityNode currentOrders = workflow.AllEntities().Single(node => node.Name == "orders");
        EntityNode audit = workflow.AllEntities().Single(node => node.Name == "audit");
        Assert.Same(orders, currentOrders);
        Assert.Same(currentOrders, workflow.SelectedEntity);
        Assert.False(currentOrders.IsExpanded);
        Assert.True(currentOrders.IsVisible);
        Assert.False(audit.IsVisible);
        Assert.Equal("12", currentOrders.DisplayMessageCount);
    }

    [Fact]
    public async Task Refresh_retains_checked_or_focused_deliveries_as_explicitly_unobserved()
    {
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var browser = new FakeBrowser(snapshot, snapshot);
        var messages = new FakeMessages();
        var address = new EntityAddress(EntityKind.Queue, "orders");
        messages.Set(address, Message(1), Message(2));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(browser, messages, snapshot), 1);
        EntityNode queue = workflow.AllEntities().Single(node => node.Name == "orders");
        await workflow.SelectAsync(queue, deadLetter: false);
        MessageRow checkedRow = workflow.Messages.Single(row => row.Key.SequenceNumber == 1);
        MessageRow focusedRow = workflow.Messages.Single(row => row.Key.SequenceNumber == 2);
        checkedRow.IsSelected = true;
        workflow.FocusedMessage = focusedRow;
        messages.Set(address, Message(2));

        await workflow.RefreshAsync();

        Assert.Same(checkedRow, workflow.Messages.Single(row => row.Key.SequenceNumber == 1));
        Assert.True(checkedRow.IsSelected);
        Assert.Equal("Active", checkedRow.StateLabel);
        Assert.Contains("not returned by the broker", checkedRow.ObservationDetail, StringComparison.Ordinal);
        Assert.Same(focusedRow, workflow.FocusedMessage);
        Assert.Same(focusedRow, workflow.Messages.Single(row => row.Key.SequenceNumber == 2));
        Assert.Empty(focusedRow.ObservationDetail);
    }

    [Fact]
    public async Task Refresh_clears_deliveries_when_the_selected_entity_is_gone()
    {
        EntityDiscoverySnapshot initial = Snapshot(Queue("orders"));
        EntityDiscoverySnapshot afterRemoval = Snapshot();
        var browser = new FakeBrowser(afterRemoval);
        var messages = new FakeMessages();
        messages.Set(new EntityAddress(EntityKind.Queue, "orders"), Message(1));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(browser, messages, initial), 1);
        EntityNode queue = workflow.AllEntities().Single(node => node.Name == "orders");
        await workflow.SelectAsync(queue, deadLetter: false);
        workflow.FocusedMessage = workflow.Messages.Single();

        await workflow.RefreshAsync();

        Assert.Null(workflow.SelectedEntity);
        Assert.Null(workflow.FocusedMessage);
        Assert.Empty(workflow.Messages);
        Assert.False(workflow.CanLoadMore);
    }

    [Fact]
    public async Task Incomplete_refresh_retains_selected_scope_and_rows_with_stale_counts()
    {
        EntityDiscoverySnapshot initial = Snapshot(Queue("orders", 4));
        EntityDiscoverySnapshot incomplete = SnapshotWithStatus(false);
        var browser = new FakeBrowser(incomplete);
        var messages = new FakeMessages();
        var address = new EntityAddress(EntityKind.Queue, "orders");
        messages.Set(address, Message(1));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(browser, messages, initial), 1);
        EntityNode orders = workflow.AllEntities().Single(node => node.Name == "orders");
        await workflow.SelectAsync(orders, deadLetter: false);
        MessageRow row = Assert.Single(workflow.Messages);
        row.IsSelected = true;
        workflow.FocusedMessage = row;

        await workflow.RefreshAsync();

        Assert.Same(orders, workflow.SelectedEntity);
        Assert.Same(row, workflow.FocusedMessage);
        Assert.Same(row, Assert.Single(workflow.Messages));
        Assert.True(row.IsSelected);
        Assert.Equal("—", orders.DisplayMessageCount);
        Assert.Contains("Discovery incomplete", orders.ActiveCountDetail, StringComparison.Ordinal);
        Assert.Contains("not returned", orders.DlqCountDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scope_switch_prevents_a_late_refresh_from_replacing_new_rows()
    {
        EntityAddress oldAddress = new(EntityKind.Queue, "old");
        EntityAddress newAddress = new(EntityKind.Queue, "new");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("old"), Queue("new"));
        var browser = new FakeBrowser(snapshot, snapshot);
        var messages = new FakeMessages();
        messages.Set(oldAddress, Message(1));
        messages.Set(newAddress, Message(2));
        var oldPeekStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldPeek = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(browser, messages, snapshot), 1);
        EntityNode oldNode = workflow.AllEntities().Single(node => node.Name == "old");
        EntityNode newNode = workflow.AllEntities().Single(node => node.Name == "new");
        await workflow.SelectAsync(oldNode, deadLetter: false);
        messages.BlockNext(oldAddress, oldPeekStarted, releaseOldPeek);

        Task refresh = workflow.RefreshAsync();
        await oldPeekStarted.Task;
        Task select = workflow.SelectAsync(newNode, deadLetter: false);
        await select;
        releaseOldPeek.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await refresh);

        Assert.Equal(newAddress, workflow.SelectedEntity?.Address);
        Assert.Equal(new[] { 2L }, workflow.Messages.Select(row => row.Key.SequenceNumber));
    }

    private static BrokerSession Session(
        FakeBrowser browser,
        FakeMessages messages,
        EntityDiscoverySnapshot snapshot)
        => new(null!, browser, messages, snapshot, null);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities)
        => SnapshotWithStatus(true, entities);

    private static EntityDiscoverySnapshot SnapshotWithStatus(bool isComplete, params ServiceBusEntityNode[] entities)
        => new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: isComplete,
            Issues: []);

    private static ServiceBusEntityNode Queue(string name, long active = 0)
        => new(
            EntityKind.Queue,
            name,
            null,
            new EntityRuntimeCounts(active, 0, 0, active),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));

    private static ExplorerMessage Message(long sequence)
        => new(
            $"message-{sequence}",
            sequence,
            $"body-{sequence}",
            $"body-{sequence}",
            7,
            null,
            null,
            0,
            null,
            null,
            null,
            null,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>());

    private sealed class FakeBrowser(params EntityDiscoverySnapshot[] snapshots) : IInvestigationEntityBrowser
    {
        private int index;

        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            int current = Math.Min(index++, snapshots.Length - 1);
            return Task.FromResult(snapshots[current]);
        }
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<EntityAddress, IReadOnlyList<ExplorerMessage>> messages = [];
        private EntityAddress? blockedAddress;
        private TaskCompletionSource<bool>? blockedStarted;
        private TaskCompletionSource<bool>? releaseBlocked;

        public void Set(EntityAddress address, params ExplorerMessage[] values) => messages[address] = values;

        public void BlockNext(
            EntityAddress address,
            TaskCompletionSource<bool> started,
            TaskCompletionSource<bool> release)
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
            if (address == blockedAddress)
            {
                blockedAddress = null;
                blockedStarted!.SetResult(true);
                await releaseBlocked!.Task;
            }

            return messages.TryGetValue(address, out IReadOnlyList<ExplorerMessage>? source)
                ? source.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value).Take(take).ToList()
                : [];
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
