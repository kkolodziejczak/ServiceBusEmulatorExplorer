using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests;

public sealed class InvestigationDiscoveryTests
{
    [Fact]
    public async Task Discovery_preserves_queue_and_topic_scheduled_counts_and_marks_subscription_scheduled_unsupported()
    {
        var admin = new FakeAdministrationClient
        {
            Queues = [Queue("orders")],
            Topics = [Topic("events")],
            Subscriptions = { ["events"] = [Subscription("events", "billing")] },
            QueueRuntime = { ["orders"] = QueueRuntime("orders", active: 2, deadLetter: 1, scheduled: 3) },
            TopicRuntime = { ["events"] = TopicRuntime("events", scheduled: 4) },
            SubscriptionRuntime = { [("events", "billing")] = SubscriptionRuntime("events", "billing", active: 5, deadLetter: 6) }
        };

        EntityDiscoverySnapshot snapshot = await DiscoverAsync(admin);

        EntityObservation queue = snapshot.Entities.Single(item => item.Entity.Kind == EntityKind.Queue);
        Assert.Equal(2, queue.Counts.Active.Value);
        Assert.Equal(1, queue.Counts.DeadLetter.Value);
        Assert.Equal(3, queue.Counts.Scheduled.Value);

        EntityObservation topic = snapshot.Entities.Single(item => item.Entity.Kind == EntityKind.Topic);
        Assert.Equal(5, topic.Counts.Active.Value);
        Assert.Equal(6, topic.Counts.DeadLetter.Value);
        Assert.Equal(4, topic.Counts.Scheduled.Value);

        EntityObservation subscription = snapshot.Entities.Single(item => item.Entity.Kind == EntityKind.Subscription);
        Assert.Equal(CountAvailability.NotSupported, subscription.Counts.Scheduled.Availability);
        Assert.Null(subscription.Counts.Scheduled.Value);
        Assert.True(snapshot.IsComplete);
    }

    [Fact]
    public async Task Runtime_failure_keeps_entity_metadata_and_reports_unavailable_counts_without_exception_details()
    {
        var admin = new FakeAdministrationClient
        {
            Queues = [Queue("orders")],
            QueueFailures = { ["orders"] = new InvalidOperationException("Endpoint=sb://secret;SharedAccessKey=do-not-log") }
        };

        EntityDiscoverySnapshot snapshot = await DiscoverAsync(admin);
        EntityObservation queue = Assert.Single(snapshot.Entities);

        Assert.Equal("orders", queue.Entity.Name);
        Assert.Equal(EntityKind.Queue, queue.Entity.Kind);
        Assert.Equal(CountAvailability.Unavailable, queue.Counts.Active.Availability);
        Assert.Null(queue.Counts.Active.Value);
        Assert.False(snapshot.IsComplete);
        Assert.NotEmpty(snapshot.Issues);
        Assert.DoesNotContain("secret", string.Join(" ", snapshot.Issues), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Request_failure_reports_safe_auth_status_without_server_detail()
    {
        var admin = new FakeAdministrationClient
        {
            Queues = [Queue("orders")],
            QueueFailures =
            {
                ["orders"] = new RequestFailedException(
                    403,
                    "Endpoint=sb://secret; SharedAccessKey=do-not-log",
                    "AuthorizationFailed",
                    null)
            }
        };

        EntityDiscoverySnapshot snapshot = await DiscoverAsync(admin);

        Assert.Contains("AuthorizationFailed (HTTP 403)", string.Join(" ", snapshot.Issues));
        Assert.DoesNotContain("secret", string.Join(" ", snapshot.Issues), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Topic_counts_are_unavailable_when_any_subscription_runtime_is_unknown()
    {
        var admin = new FakeAdministrationClient
        {
            Topics = [Topic("events")],
            Subscriptions = { ["events"] = [Subscription("events", "billing"), Subscription("events", "audit")] },
            TopicRuntime = { ["events"] = TopicRuntime("events", scheduled: 0) },
            SubscriptionRuntime = { [("events", "billing")] = SubscriptionRuntime("events", "billing", active: 2, deadLetter: 1) },
            SubscriptionFailures = { [("events", "audit")] = new InvalidOperationException("runtime unavailable") }
        };

        EntityDiscoverySnapshot snapshot = await DiscoverAsync(admin);
        EntityObservation topic = snapshot.Entities.Single(item => item.Entity.Kind == EntityKind.Topic);

        Assert.Equal(CountAvailability.Unavailable, topic.Counts.Active.Availability);
        Assert.Equal(CountAvailability.Unavailable, topic.Counts.DeadLetter.Availability);
        Assert.Equal(CountAvailability.Known, topic.Counts.Scheduled.Availability);
        Assert.Equal(2, snapshot.Entities.Count(item => item.Entity.Kind == EntityKind.Subscription));
    }

    private static async Task<EntityDiscoverySnapshot> DiscoverAsync(FakeAdministrationClient admin) =>
        await new InvestigationEntityBrowser(new FakeClientFactory(admin)).DiscoverAsync(CancellationToken.None);

    private static QueueProperties Queue(string name) =>
        ServiceBusModelFactory.QueueProperties(name, status: EntityStatus.Active, maxDeliveryCount: 10, userMetadata: "",
            lockDuration: TimeSpan.FromSeconds(30), defaultMessageTimeToLive: TimeSpan.FromDays(1),
            autoDeleteOnIdle: TimeSpan.FromDays(30), duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10));

    private static TopicProperties Topic(string name) =>
        ServiceBusModelFactory.TopicProperties(name, status: EntityStatus.Active,
            defaultMessageTimeToLive: TimeSpan.FromDays(1), autoDeleteOnIdle: TimeSpan.FromDays(30),
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10));

    private static SubscriptionProperties Subscription(string topic, string name) =>
        ServiceBusModelFactory.SubscriptionProperties(topic, name, status: EntityStatus.Active, maxDeliveryCount: 10, userMetadata: "",
            lockDuration: TimeSpan.FromSeconds(30), defaultMessageTimeToLive: TimeSpan.FromDays(1), autoDeleteOnIdle: TimeSpan.FromDays(30));

    private static QueueRuntimeProperties QueueRuntime(string name, long active, long deadLetter, long scheduled) =>
        ServiceBusModelFactory.QueueRuntimeProperties(name, activeMessageCount: active, deadLetterMessageCount: deadLetter, scheduledMessageCount: scheduled);

    private static TopicRuntimeProperties TopicRuntime(string name, long scheduled) =>
        ServiceBusModelFactory.TopicRuntimeProperties(name, scheduledMessageCount: scheduled);

    private static SubscriptionRuntimeProperties SubscriptionRuntime(string topic, string name, long active, long deadLetter) =>
        ServiceBusModelFactory.SubscriptionRuntimeProperties(topic, name, activeMessageCount: active, deadLetterMessageCount: deadLetter);

    private sealed class FakeClientFactory(FakeAdministrationClient admin) : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => admin;

        public ServiceBusClient RuntimeClient => null!;

        public Task ConnectAsync(
            ServiceBusEmulatorExplorer.Core.Connection.ConnectionProfile profile,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAdministrationClient : ServiceBusAdministrationClient
    {
        public IReadOnlyList<QueueProperties> Queues { get; init; } = [];
        public IReadOnlyList<TopicProperties> Topics { get; init; } = [];
        public Dictionary<string, IReadOnlyList<SubscriptionProperties>> Subscriptions { get; } = [];
        public Dictionary<string, QueueRuntimeProperties> QueueRuntime { get; } = [];
        public Dictionary<string, TopicRuntimeProperties> TopicRuntime { get; } = [];
        public Dictionary<(string Topic, string Subscription), SubscriptionRuntimeProperties> SubscriptionRuntime { get; } = [];
        public Dictionary<string, Exception> QueueFailures { get; } = [];
        public Dictionary<(string Topic, string Subscription), Exception> SubscriptionFailures { get; } = [];

        public override AsyncPageable<QueueProperties> GetQueuesAsync(CancellationToken cancellationToken) =>
            AsyncPageable<QueueProperties>.FromPages([Page<QueueProperties>.FromValues(Queues, null, new TestResponse())]);

        public override AsyncPageable<TopicProperties> GetTopicsAsync(CancellationToken cancellationToken) =>
            AsyncPageable<TopicProperties>.FromPages([Page<TopicProperties>.FromValues(Topics, null, new TestResponse())]);

        public override AsyncPageable<SubscriptionProperties> GetSubscriptionsAsync(string topicName, CancellationToken cancellationToken) =>
            AsyncPageable<SubscriptionProperties>.FromPages([
                Page<SubscriptionProperties>.FromValues(
                    Subscriptions.TryGetValue(topicName, out IReadOnlyList<SubscriptionProperties>? subscriptions)
                        ? subscriptions
                        : [],
                    null,
                    new TestResponse())]);

        public override Task<Response<QueueRuntimeProperties>> GetQueueRuntimePropertiesAsync(string name, CancellationToken cancellationToken)
        {
            if (QueueFailures.TryGetValue(name, out Exception? failure))
            {
                return Task.FromException<Response<QueueRuntimeProperties>>(failure);
            }

            return Task.FromResult<Response<QueueRuntimeProperties>>(
                Response.FromValue(QueueRuntime[name], new TestResponse()));
        }

        public override Task<Response<TopicRuntimeProperties>> GetTopicRuntimePropertiesAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<Response<TopicRuntimeProperties>>(
                Response.FromValue(TopicRuntime[name], new TestResponse()));

        public override Task<Response<SubscriptionRuntimeProperties>> GetSubscriptionRuntimePropertiesAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            if (SubscriptionFailures.TryGetValue((topicName, subscriptionName), out Exception? failure))
            {
                return Task.FromException<Response<SubscriptionRuntimeProperties>>(failure);
            }

            return Task.FromResult<Response<SubscriptionRuntimeProperties>>(
                Response.FromValue(SubscriptionRuntime[(topicName, subscriptionName)], new TestResponse()));
        }
    }

    private sealed class TestResponse : Response
    {
        private readonly Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        private Stream? contentStream;
        private string clientRequestId = string.Empty;

        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get => contentStream; set => contentStream = value; }
        public override string ClientRequestId { get => clientRequestId; set => clientRequestId = value; }
        public override void Dispose() => contentStream?.Dispose();
        protected override bool TryGetHeader(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => headers.TryGetValue(name, out value);
        protected override bool TryGetHeaderValues(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<string>? values)
        {
            if (headers.TryGetValue(name, out string? value))
            {
                values = [value];
                return true;
            }

            values = null;
            return false;
        }
        protected override bool ContainsHeader(string name) => headers.ContainsKey(name);
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => headers.Select(item => new HttpHeader(item.Key, item.Value));
    }
}
