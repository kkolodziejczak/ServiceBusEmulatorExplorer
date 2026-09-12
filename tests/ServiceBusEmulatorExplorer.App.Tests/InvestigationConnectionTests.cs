using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationConnectionTests
{
    [Fact]
    public async Task ConnectAsync_AdminSucceedsButRuntimeProbeFails_DoesNotReturnSessionAndDisposesFactory()
    {
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages
        {
            PeekFailure = new InvalidOperationException("runtime access failed")
        };
        var workflow = CreateWorkflow(factory, browser, messages);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.ConnectAsync(Profile(), CancellationToken.None));

        Assert.Equal("runtime access failed", failure.Message);
        Assert.Equal(1, factory.ConnectCount);
        Assert.Equal(1, browser.DiscoverCount);
        Assert.Single(messages.PeekCalls);
        Assert.True(factory.IsDisposed);
    }

    [Fact]
    public async Task ConnectAsync_WhenCancellationIsRequestedBeforeReturn_DisposesFactoryAndRethrowsCancellation()
    {
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages();
        var workflow = CreateWorkflow(factory, browser, messages);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => workflow.ConnectAsync(Profile(), new CancellationToken(canceled: true)));

        Assert.Equal(1, factory.ConnectCount);
        Assert.True(factory.IsDisposed);
        Assert.Equal(1, browser.DiscoverCount);
        Assert.Single(messages.PeekCalls);
    }

    [Fact]
    public async Task ConnectAsync_WhenDiscoveryHasNoReceivingEntity_ReturnsWarningWithoutRuntimeProbe()
    {
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Topic("events")));
        var messages = new FakeMessages();
        var workflow = CreateWorkflow(factory, browser, messages);

        {
            await using BrokerSession session = await workflow.ConnectAsync(Profile(), CancellationToken.None);

            Assert.Equal(
                "Administration is available; runtime access cannot be verified without a queue or subscription.",
                session.ReadinessWarning);
            Assert.Empty(messages.PeekCalls);
            Assert.False(factory.IsDisposed);
        }

        Assert.True(factory.IsDisposed);
    }

    [Fact]
    public async Task ConnectAsync_WhenQueueReportsZeroMessages_StillPerformsReadOnlyPeek()
    {
        var factory = new FakeFactory();
        var queue = Queue("orders");
        var browser = new FakeBrowser(Snapshot(queue));
        var messages = new FakeMessages();
        var workflow = CreateWorkflow(factory, browser, messages);

        await using BrokerSession session = await workflow.ConnectAsync(Profile(), CancellationToken.None);

        EntityAddress address = Assert.Single(messages.PeekCalls).Address;
        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), address);
        Assert.Equal(MessageBucket.Active, messages.PeekCalls[0].Bucket);
        Assert.Equal(1, messages.PeekCalls[0].Take);
        Assert.Null(messages.PeekCalls[0].FromSequenceNumber);
    }

    [Fact]
    public async Task ConnectAsync_WhenSubscriptionIsProbe_PassesTopicNameInRuntimeAddress()
    {
        var factory = new FakeFactory();
        var subscription = Subscription("events", "billing");
        var browser = new FakeBrowser(Snapshot(subscription));
        var messages = new FakeMessages();
        var workflow = CreateWorkflow(factory, browser, messages);

        await using BrokerSession session = await workflow.ConnectAsync(Profile(), CancellationToken.None);

        Assert.Equal(
            new EntityAddress(EntityKind.Subscription, "billing", "events"),
            Assert.Single(messages.PeekCalls).Address);
    }

    [Fact]
    public async Task BrokerSession_DisposeAsync_DisposesFactoryAfterSuccessfulConnect()
    {
        var factory = new FakeFactory();
        var browser = new FakeBrowser(Snapshot(Queue("orders")));
        var messages = new FakeMessages();
        var workflow = CreateWorkflow(factory, browser, messages);

        BrokerSession session = await workflow.ConnectAsync(Profile(), CancellationToken.None);
        Assert.False(factory.IsDisposed);

        await session.DisposeAsync();

        Assert.True(factory.IsDisposed);
        Assert.Equal(1, factory.DisposeCount);
    }

    private static BrokerConnectionWorkflow CreateWorkflow(
        FakeFactory factory,
        FakeBrowser browser,
        FakeMessages messages) =>
        new(
            () => factory,
            _ => browser,
            _ => messages);

    private static EntityDiscoverySnapshot Snapshot(params ServiceBusEntityNode[] entities) =>
        new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known),
                    new(0, CountAvailability.Known)))).ToArray(),
            DateTimeOffset.UtcNow,
            IsComplete: true,
            Issues: []);

    private static ServiceBusEntityNode Queue(string name) =>
        new(
            EntityKind.Queue,
            name,
            TopicName: null,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));

    private static ServiceBusEntityNode Topic(string name) =>
        new(
            EntityKind.Topic,
            name,
            TopicName: null,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));

    private static ServiceBusEntityNode Subscription(string topicName, string name) =>
        new(
            EntityKind.Subscription,
            name,
            topicName,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata($"{topicName}/Subscriptions/{name}", "Active", null, null, null, null, null, null, null));

    private static ConnectionProfile Profile() =>
        new("test", "runtime", "administration");

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public Exception? ConnectFailure { get; init; }

        public int ConnectCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsDisposed => DisposeCount > 0;

        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;

        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;

        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            ConnectCount++;
            return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public int DiscoverCount { get; private set; }

        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
        {
            DiscoverCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        public Exception? PeekFailure { get; init; }

        public List<PeekCall> PeekCalls { get; } = [];

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            PeekCalls.Add(new(address, bucket, take, fromSequenceNumber));
            return PeekFailure is null
                ? Task.FromResult<IReadOnlyList<ExplorerMessage>>([])
                : Task.FromException<IReadOnlyList<ExplorerMessage>>(PeekFailure);
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record PeekCall(
        EntityAddress Address,
        MessageBucket Bucket,
        int Take,
        long? FromSequenceNumber);
}
