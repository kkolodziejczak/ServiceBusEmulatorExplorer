using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationKnownDeliveryTests
{
    [Fact]
    public async Task OpenKnown_ignores_a_delivery_from_an_older_connection_generation()
    {
        EntityAddress address = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, Message(1));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(snapshot, messages), connectionGeneration: 2);
        MessageRow known = Row(address, generation: 1, sequence: 1);

        await workflow.OpenKnownAsync(known);

        Assert.Null(workflow.SelectedEntity);
        Assert.Empty(workflow.Messages);
        Assert.Empty(messages.Calls);
    }

    [Fact]
    public async Task OpenKnown_retains_a_known_delivery_missing_from_the_first_page_as_unobserved()
    {
        EntityAddress address = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, Message(2));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(snapshot, messages), connectionGeneration: 1);
        MessageRow known = Row(address, generation: 1, sequence: 1);

        await workflow.OpenKnownAsync(known);

        MessageRow retained = Assert.Single(workflow.Messages, row => row.Key.SequenceNumber == 1);
        Assert.Contains("not returned by the broker", retained.ObservationDetail, StringComparison.Ordinal);
        Assert.Same(retained, workflow.FocusedMessage);
        Assert.Equal(new[] { 2L, 1L }, workflow.Messages.Select(row => row.Key.SequenceNumber));
    }

    [Fact]
    public async Task OpenKnown_reuses_a_delivery_row_that_is_already_observed()
    {
        EntityAddress address = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, Message(1));
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(snapshot, messages), connectionGeneration: 1);
        EntityNode queue = workflow.AllEntities().Single();
        await workflow.SelectAsync(queue, deadLetter: false);
        MessageRow observed = Assert.Single(workflow.Messages);

        await workflow.OpenKnownAsync(observed);

        Assert.Same(observed, Assert.Single(workflow.Messages));
        Assert.Same(observed, workflow.FocusedMessage);
        Assert.Empty(observed.ObservationDetail);
    }

    [Fact]
    public async Task Session_change_during_open_known_await_blocks_late_old_publication()
    {
        EntityAddress address = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        messages.Set(address, Message(1));
        messages.BlockNext(address, started, release);
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(snapshot, messages), connectionGeneration: 1);
        MessageRow known = Row(address, generation: 1, sequence: 1);

        Task open = workflow.OpenKnownAsync(known);
        await started.Task;
        workflow.SetSession(null, connectionGeneration: 2);
        release.SetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await open);
        Assert.Null(workflow.SelectedEntity);
        Assert.Empty(workflow.Messages);
        Assert.Null(workflow.FocusedMessage);
    }

    [Fact]
    public async Task Navigation_during_open_known_await_blocks_late_old_publication()
    {
        EntityAddress oldAddress = new(EntityKind.Queue, "old");
        EntityAddress newAddress = new(EntityKind.Queue, "new");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("old"), Queue("new"));
        var messages = new FakeMessages();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        messages.Set(oldAddress, Message(1));
        messages.Set(newAddress, Message(2));
        messages.BlockNext(oldAddress, started, release);
        var workflow = new MessageBrowseWorkflow();
        workflow.SetSession(Session(snapshot, messages), connectionGeneration: 1);
        EntityNode newNode = workflow.AllEntities().Single(node => node.Address == newAddress);
        MessageRow known = Row(oldAddress, generation: 1, sequence: 1);

        Task open = workflow.OpenKnownAsync(known);
        await started.Task;
        Task navigation = workflow.SelectAsync(newNode, deadLetter: false);
        await navigation;
        release.SetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await open);
        Assert.Same(newNode, workflow.SelectedEntity);
        Assert.Equal(new[] { 2L }, workflow.Messages.Select(row => row.Key.SequenceNumber));
        Assert.Equal(newAddress, workflow.FocusedMessage?.Key.Source);
    }

    private static BrokerSession Session(EntityDiscoverySnapshot snapshot, FakeMessages messages)
        => new(null!, new FakeBrowser(snapshot), messages, snapshot, null);

    private static MessageRow Row(EntityAddress address, long generation, long sequence)
        => new(
            new MessageDelivery(
                new DeliveryIdentity(generation, address, MessageBucket.Active, sequence),
                Message(sequence)),
            TimestampDisplay.Utc);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities)
        => new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);

    private static ServiceBusEntityNode Queue(string name)
        => new(EntityKind.Queue, name, null, new(0, 0, 0, 0), new(name, "Active", null, null, null, null, null, null, null));

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

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
            => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<EntityAddress, IReadOnlyList<ExplorerMessage>> messages = [];
        private EntityAddress? blockedAddress;
        private TaskCompletionSource<bool>? started;
        private TaskCompletionSource<bool>? release;

        public List<Call> Calls { get; } = [];

        public void Set(EntityAddress address, params ExplorerMessage[] values) => messages[address] = values;

        public void BlockNext(
            EntityAddress address,
            TaskCompletionSource<bool> startSignal,
            TaskCompletionSource<bool> releaseSignal)
        {
            blockedAddress = address;
            started = startSignal;
            release = releaseSignal;
        }

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            Calls.Add(new(address, bucket, take, fromSequenceNumber));
            if (address == blockedAddress)
            {
                blockedAddress = null;
                started!.SetResult(true);
                await release!.Task;
            }

            IReadOnlyList<ExplorerMessage> values = messages.TryGetValue(address, out IReadOnlyList<ExplorerMessage>? found)
                ? found
                : [];
            return values
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToList();
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public sealed record Call(EntityAddress Address, MessageBucket Bucket, int Take, long? FromSequenceNumber);
    }
}
