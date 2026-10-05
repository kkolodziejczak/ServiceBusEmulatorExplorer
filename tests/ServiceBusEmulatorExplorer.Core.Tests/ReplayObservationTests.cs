using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class ReplayObservationTests
{
    [Fact]
    public async Task Complete_absence_scans_active_and_dead_letter_for_queues_and_every_subscription()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityAddress billing = new(EntityKind.Subscription, "billing", "events");
        EntityAddress fulfillment = new(EntityKind.Subscription, "fulfillment", "events");
        var browser = new Browser(EntitySnapshot(true, Queue("orders"), Topic("events"),
            Subscription("billing", "events"), Subscription("fulfillment", "events")));
        var service = new InMemoryMessageService();
        var observer = new ReplayObserver(browser, service);

        ReplayObservationBatch batch = await observer.ObserveAsync(["replay-32"], 12, 100,
            TimeSpan.FromMinutes(1), CancellationToken.None);

        ReplayObservation observation = Assert.Single(batch.Observations).Value;
        Assert.True(observation.IsComplete);
        Assert.Empty(observation.Locations);
        Assert.Null(observation.Limitation);
        Assert.Contains(service.Calls, call => call.Address == queue && call.Bucket == MessageBucket.Active);
        Assert.Contains(service.Calls, call => call.Address == queue && call.Bucket == MessageBucket.DeadLetter);
        Assert.Contains(service.Calls, call => call.Address == billing && call.Bucket == MessageBucket.Active);
        Assert.Contains(service.Calls, call => call.Address == billing && call.Bucket == MessageBucket.DeadLetter);
        Assert.Contains(service.Calls, call => call.Address == fulfillment && call.Bucket == MessageBucket.Active);
        Assert.Contains(service.Calls, call => call.Address == fulfillment && call.Bucket == MessageBucket.DeadLetter);
        Assert.DoesNotContain(service.Calls, call => call.Address.Kind == EntityKind.Topic);
        Assert.True(observation.SourceCount > 0);
    }

    [Fact]
    public async Task Incomplete_discovery_never_reports_absence()
    {
        var browser = new Browser(EntitySnapshot(false, Queue("orders")), "Subscription discovery was incomplete.");
        var observer = new ReplayObserver(browser, new InMemoryMessageService());

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            ["replay-32"], 4, 100, TimeSpan.FromMinutes(1), CancellationToken.None)).Observations).Value;

        Assert.False(observation.IsComplete);
        Assert.Empty(observation.Locations);
        Assert.False(string.IsNullOrWhiteSpace(observation.Limitation));
    }

    [Fact]
    public async Task Discovery_error_never_reports_absence()
    {
        var browser = new Browser(EntitySnapshot(true, Queue("orders")), "Discovery unavailable.");
        var observer = new ReplayObserver(browser, new InMemoryMessageService());

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            ["replay-32"], 4, 100, TimeSpan.FromMinutes(1), CancellationToken.None)).Observations).Value;

        Assert.False(observation.IsComplete);
        Assert.Empty(observation.Locations);
        Assert.False(string.IsNullOrWhiteSpace(observation.Limitation));
    }

    [Fact]
    public async Task Delivery_budget_exhaustion_never_reports_absence()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        var browser = new Browser(EntitySnapshot(true, Queue("orders")));
        var service = new InMemoryMessageService();
        service.Add(queue, MessageBucket.Active, Message("unrelated-1", 1), Message("unrelated-2", 2));
        var observer = new ReplayObserver(browser, service);

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            ["replay-32"], 4, 1, TimeSpan.FromMinutes(1), CancellationToken.None)).Observations).Value;

        Assert.False(observation.IsComplete);
        Assert.Equal(1, observation.ScannedDeliveries);
        Assert.Empty(observation.Locations);
        Assert.False(string.IsNullOrWhiteSpace(observation.Limitation));
    }

    [Fact]
    public async Task Time_budget_exhaustion_never_reports_absence()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        var browser = new Browser(EntitySnapshot(true, Queue("orders")));
        var service = new InMemoryMessageService { Delay = TimeSpan.FromMilliseconds(250) };
        var observer = new ReplayObserver(browser, service);

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            ["replay-32"], 4, 100, TimeSpan.FromMilliseconds(30), CancellationToken.None)).Observations).Value;

        Assert.False(observation.IsComplete);
        Assert.Empty(observation.Locations);
        Assert.False(string.IsNullOrWhiteSpace(observation.Limitation));
    }

    [Fact]
    public async Task Source_error_never_reports_absence()
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        var browser = new Browser(EntitySnapshot(true, Queue("orders")));
        var service = new InMemoryMessageService { Failure = (queue, MessageBucket.DeadLetter) };
        var observer = new ReplayObserver(browser, service);

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            ["replay-32"], 4, 100, TimeSpan.FromMinutes(1), CancellationToken.None)).Observations).Value;

        Assert.False(observation.IsComplete);
        Assert.Empty(observation.Locations);
        Assert.False(string.IsNullOrWhiteSpace(observation.Limitation));
    }

    [Fact]
    public async Task Quoted_requested_ids_match_exact_message_ids_without_query_expansion()
    {
        const string hostileId = "order 42\" OR message:\"fake";
        const string secondId = "invoice 7";
        EntityAddress queue = new(EntityKind.Queue, "orders");
        var browser = new Browser(EntitySnapshot(true, Queue("orders")));
        var service = new InMemoryMessageService();
        service.Add(queue, MessageBucket.Active,
            Message(hostileId, 1),
            Message(hostileId + "-suffix", 2),
            Message("different-id", 3, correlationId: hostileId),
            Message("fake", 4));
        service.Add(queue, MessageBucket.DeadLetter, Message(secondId, 5));
        var observer = new ReplayObserver(browser, service);

        ReplayObservationBatch batch = await observer.ObserveAsync(
            [hostileId, secondId], 9, 100, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(2, batch.Observations.Count);
        Assert.True(batch.Observations[hostileId].IsComplete);
        var hostileLocation = Assert.Single(batch.Observations[hostileId].Locations);
        Assert.Equal(queue, hostileLocation.Source);
        Assert.Equal(MessageBucket.Active, hostileLocation.Bucket);
        Assert.True(batch.Observations[secondId].IsComplete);
        var secondLocation = Assert.Single(batch.Observations[secondId].Locations);
        Assert.Equal(queue, secondLocation.Source);
        Assert.Equal(MessageBucket.DeadLetter, secondLocation.Bucket);
        Assert.DoesNotContain(batch.Observations.Keys, id => id == "fake" || id == hostileId + "-suffix");
    }

    [Fact]
    public async Task Found_replay_id_reports_every_queue_subscription_and_bucket_sibling()
    {
        const string replayId = "order-42-replay-3";
        EntityAddress queue = new(EntityKind.Queue, "orders");
        EntityAddress billing = new(EntityKind.Subscription, "billing", "events");
        EntityAddress fulfillment = new(EntityKind.Subscription, "fulfillment", "events");
        var browser = new Browser(EntitySnapshot(true, Queue("orders"), Topic("events"),
            Subscription("billing", "events"), Subscription("fulfillment", "events")));
        var service = new InMemoryMessageService();
        service.Add(queue, MessageBucket.Active, Message(replayId, 1));
        service.Add(billing, MessageBucket.Active, Message(replayId, 2));
        service.Add(fulfillment, MessageBucket.DeadLetter, Message(replayId, 3));
        var observer = new ReplayObserver(browser, service);

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            [replayId], 8, 100, TimeSpan.FromMinutes(1), CancellationToken.None)).Observations).Value;

        Assert.True(observation.IsComplete);
        Assert.Equal(3, observation.Locations.Count);
        Assert.Contains(observation.Locations, location => location.Source == queue && location.Bucket == MessageBucket.Active);
        Assert.Contains(observation.Locations, location => location.Source == billing && location.Bucket == MessageBucket.Active);
        Assert.Contains(observation.Locations, location => location.Source == fulfillment && location.Bucket == MessageBucket.DeadLetter);
        Assert.All(observation.Locations.Where(location => location.Bucket == MessageBucket.Active),
            location => Assert.Equal("Main queue", location.State));
        Assert.Equal("Dead letter", observation.Locations.Single(location => location.Bucket == MessageBucket.DeadLetter).State);
    }

    [Theory]
    [InlineData(ServiceBusMessageState.Deferred)]
    [InlineData(ServiceBusMessageState.Scheduled)]
    public void Message_projection_preserves_known_broker_state(ServiceBusMessageState state)
    {
        ServiceBusReceivedMessage received = Received("state-projection", 21, state);

        ExplorerMessage projected = MessageProjection.Create(received);

        Assert.Equal(state, projected.BrokerState);
    }

    [Theory]
    [InlineData(ServiceBusMessageState.Deferred, "Deferred")]
    [InlineData(ServiceBusMessageState.Scheduled, "Scheduled")]
    public async Task Observer_UsesKnownDeferredAndScheduledStateLabels(ServiceBusMessageState state, string expectedLabel)
    {
        EntityAddress queue = new(EntityKind.Queue, "orders");
        var browser = new Browser(EntitySnapshot(true, Queue("orders")));
        var service = new InMemoryMessageService();
        service.Add(queue, MessageBucket.Active, MessageProjection.Create(Received("state-observed", 21, state)));
        var observer = new ReplayObserver(browser, service);

        ReplayObservation observation = Assert.Single((await observer.ObserveAsync(
            ["state-observed"], 3, 100, TimeSpan.FromMinutes(1), CancellationToken.None)).Observations).Value;

        Assert.True(observation.IsComplete);
        Assert.Equal(expectedLabel, Assert.Single(observation.Locations).State);
    }

    private static EntityDiscoverySnapshot EntitySnapshot(bool complete, params EntityObservation[] entities) =>
        new(entities, DateTimeOffset.UtcNow, complete, complete ? [] : ["Incomplete discovery."]);

    private static EntityObservation Queue(string name) => Entity(EntityKind.Queue, name);
    private static EntityObservation Topic(string name) => Entity(EntityKind.Topic, name);
    private static EntityObservation Subscription(string name, string topic) => Entity(EntityKind.Subscription, name, topic);

    private static EntityObservation Entity(EntityKind kind, string name, string? topic = null) =>
        new(new DiscoveredEntity(kind, name, topic,
                new EntityMetadata(name, "Active", null, null, null, null, null, null, null)),
            new EntityCountObservation(new(0, CountAvailability.Known), new(0, CountAvailability.Known),
                new(0, CountAvailability.Known)));

    private static ExplorerMessage Message(string id, long sequence, string? correlationId = null) => new(
        id, sequence, id, id, id.Length, null, null, 0, "application/json", correlationId, null, id,
        new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private static ServiceBusReceivedMessage Received(string id, long sequence, ServiceBusMessageState state) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("state"), messageId: id,
            sequenceNumber: sequence, serviceBusMessageState: state);

    private sealed class Browser(EntityDiscoverySnapshot snapshot, string? error = null) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            error is null ? Task.FromResult(snapshot) : Task.FromException<EntityDiscoverySnapshot>(new InvalidOperationException(error));
    }

    private sealed class InMemoryMessageService : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> messages = [];
        public (EntityAddress Address, MessageBucket Bucket)? Failure { get; init; }
        public TimeSpan Delay { get; init; }
        public List<(EntityAddress Address, MessageBucket Bucket)> Calls { get; } = [];

        public void Add(EntityAddress address, MessageBucket bucket, params ExplorerMessage[] values) => messages[(address, bucket)] = values;

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            Calls.Add((address, bucket));
            if (Failure is { } failure && failure.Address == address && failure.Bucket == bucket)
                throw new InvalidOperationException("simulated peek failure");
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            IReadOnlyList<ExplorerMessage> result = messages.TryGetValue((address, bucket), out var found)
                ? found.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                    .Take(take).ToArray()
                : [];
            return result;
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Observation must use non-consuming peeks only.");
    }
}
