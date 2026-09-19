using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.UiSmoke.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.UiSmoke.Tests;

/// <summary>
/// Daily-use audit cases for the production browse, connection, and workspace workflows.
/// These cases use unique broker entities and real emulator operations. The expected
/// contract is deliberately stated in terms of observable work: peeking is non-consuming,
/// a refresh reconciles the visible page, source and bucket identity remain distinct, and
/// a numeric discovery count must agree with an independent peek (an unavailable count is
/// an acceptable broker limitation).
/// </summary>
public sealed class DailyBrowseAuditTests
{
    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Connect_and_open_empty_queue_shows_empty_page_without_failure() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.OpenQueueAsync();
            Assert.Empty(fixture.Workspace.Browse.Messages);
            Assert.False(fixture.Workspace.Browse.CanLoadMore);
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task External_send_then_refresh_adds_the_new_message() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.OpenQueueAsync();
            await fixture.SendQueueAsync("external-send", "refresh-me");
            await fixture.Workspace.Browse.RefreshAsync();
            Assert.Contains(fixture.Workspace.Browse.Messages, row => row.MessageId == "external-send");
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Repeated_refresh_is_non_consuming_and_preserves_the_visible_set() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.SendQueueAsync("repeat-1", "one");
            await fixture.SendQueueAsync("repeat-2", "two");
            await fixture.OpenQueueAsync();
            var first = fixture.Workspace.Browse.Messages.Select(row => row.MessageId).ToArray();
            await fixture.Workspace.Browse.RefreshAsync();
            await fixture.Workspace.Browse.RefreshAsync();
            var final = fixture.Workspace.Browse.Messages.Select(row => row.MessageId).ToArray();
            Assert.Equal(first, final);
            Assert.Equal(2, await fixture.PeekCountAsync(new(EntityKind.Queue, fixture.QueueName), MessageBucket.Active));
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Active_discovery_count_never_claims_zero_when_peek_observes_messages() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.SendQueueAsync("count-target", "counted");
            await fixture.OpenQueueAsync();
            EntityNode node = fixture.QueueNode!;
            long observed = await fixture.PeekCountAsync(node.Address!, MessageBucket.Active);
            Assert.True(observed > 0);
            CountObservation count = node.Observation!.Counts.Active;
            Assert.True(count.Availability != CountAvailability.Known || count.Value == observed,
                $"Discovery reported {count.Value} while an independent peek observed {observed}. " +
                "A broker limitation must be represented as unavailable.");
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Dead_letter_page_and_discovery_count_agree_with_independent_peek() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.SendQueueAsync("dlq-target", "will-be-dead-lettered");
            await fixture.MoveOneQueueMessageToDeadLetterAsync();
            await fixture.OpenQueueAsync(deadLetter: true);
            EntityNode node = fixture.QueueNode!;
            long observed = await fixture.PeekCountAsync(node.Address!, MessageBucket.DeadLetter);
            Assert.Equal(1, observed);
            CountObservation count = node.Observation!.Counts.DeadLetter;
            Assert.True(count.Availability != CountAvailability.Known || count.Value == observed,
                $"Discovery reported DLQ {count.Value} while an independent peek observed {observed}.");
            Assert.Contains(fixture.Workspace.Browse.Messages, row => row.IsDeadLetter && row.MessageId == "dlq-target");
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Topic_discovery_count_never_claims_zero_when_subscription_peek_observes_messages() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateTopicAsync();
        try
        {
            await fixture.SendTopicAsync("topic-count");
            await fixture.OpenTopicAsync();
            EntityNode node = fixture.TopicNodeForAudit!;
            long observed = fixture.Workspace.Browse.Messages.Count;
            Assert.True(observed > 0);
            CountObservation count = node.Observation!.Counts.Active;
            Assert.True(count.Availability != CountAvailability.Known || count.Value == observed,
                $"Topic discovery reported {count.Value} while topic browse observed {observed} deliveries.");
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Subscription_discovery_count_never_claims_zero_when_peek_observes_messages() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateTopicAsync();
        try
        {
            await fixture.SendTopicAsync("subscription-count");
            await fixture.OpenSubscriptionAsync(fixture.SubscriptionOne);
            EntityNode node = fixture.SubscriptionNodeForAudit(fixture.SubscriptionOne);
            long observed = fixture.Workspace.Browse.Messages.Count;
            Assert.True(observed > 0);
            CountObservation count = node.Observation!.Counts.Active;
            Assert.True(count.Availability != CountAvailability.Known || count.Value == observed,
                $"Subscription discovery reported {count.Value} while subscription browse observed {observed} deliveries.");
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Queue_pagination_reaches_the_end_without_duplicates_or_gaps() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync(queuePageSize: 2);
        try
        {
            for (int index = 0; index < 5; index++) await fixture.SendQueueAsync($"page-{index}", $"body-{index}");
            await fixture.OpenQueueAsync();
            while (fixture.Workspace.Browse.CanLoadMore) await fixture.Workspace.Browse.LoadMoreAsync();
            Assert.Equal(5, fixture.Workspace.Browse.Messages.Count);
            Assert.Equal(5, fixture.Workspace.Browse.Messages.Select(row => row.MessageId).Distinct().Count());
            Assert.Equal(Enumerable.Range(0, 5).Select(index => $"page-{index}"),
                fixture.Workspace.Browse.Messages.Select(row => row.MessageId));
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Topic_browse_paginates_across_subscriptions_without_losing_source_identity() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateTopicAsync(topicPageSize: 2);
        try
        {
            await fixture.SendTopicAsync("topic-page-1");
            await fixture.SendTopicAsync("topic-page-2");
            await fixture.OpenTopicAsync();
            while (fixture.Workspace.Browse.CanLoadMore) await fixture.Workspace.Browse.LoadMoreAsync();
            Assert.Equal(4, fixture.Workspace.Browse.Messages.Count);
            Assert.Equal(4, fixture.Workspace.Browse.Messages.Select(row => row.Key).Distinct().Count());
            Assert.Equal(2, fixture.Workspace.Browse.Messages.Count(row => row.Source.EndsWith("/first", StringComparison.Ordinal)));
            Assert.Equal(2, fixture.Workspace.Browse.Messages.Count(row => row.Source.EndsWith("/second", StringComparison.Ordinal)));
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Same_message_id_in_two_subscriptions_remains_two_distinct_deliveries() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateTopicAsync();
        try
        {
            await fixture.SendTopicAsync("shared-id");
            await fixture.OpenTopicAsync();
            Assert.Equal(2, fixture.Workspace.Browse.Messages.Count(row => row.MessageId == "shared-id"));
            Assert.Equal(2, fixture.Workspace.Browse.Messages.Where(row => row.MessageId == "shared-id").Select(row => row.Key).Distinct().Count());
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Switching_entities_clears_rows_from_the_previous_source() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateTwoQueuesAsync();
        try
        {
            await fixture.SendQueueAsync("first-only", "first", queue: fixture.QueueName);
            await fixture.SendQueueAsync("second-only", "second", queue: fixture.SecondQueueName);
            await fixture.OpenQueueAsync();
            await fixture.OpenSecondQueueAsync();
            Assert.Equal(fixture.SecondQueueName, fixture.Workspace.Browse.EntityPath);
            Assert.All(fixture.Workspace.Browse.Messages, row => Assert.Equal(fixture.SecondQueueName, row.Source));
            Assert.DoesNotContain(fixture.Workspace.Browse.Messages, row => row.MessageId == "first-only");
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Refresh_retains_a_focused_row_when_the_broker_no_longer_returns_it() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.SendQueueAsync("externally-removed", "remove-me");
            await fixture.OpenQueueAsync();
            MessageRow row = Assert.Single(fixture.Workspace.Browse.Messages);
            fixture.Workspace.Browse.FocusedMessage = row;
            await fixture.ReceiveAndCompleteQueueMessageAsync();
            await fixture.Workspace.Browse.RefreshAsync();
            MessageRow retained = Assert.Single(fixture.Workspace.Browse.Messages);
            Assert.Equal("externally-removed", retained.MessageId);
            Assert.Contains("not returned", retained.ObservationDetail, StringComparison.OrdinalIgnoreCase);
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Active_and_dead_letter_rows_with_the_same_message_id_have_distinct_buckets() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.SendQueueAsync("same-id", "active");
            await fixture.SendQueueAsync("same-id", "dead-letter");
            await fixture.MoveOneQueueMessageToDeadLetterAsync();
            await fixture.OpenQueueAsync();
            var active = Assert.Single(fixture.Workspace.Browse.Messages);
            await fixture.OpenQueueAsync(deadLetter: true);
            var dead = Assert.Single(fixture.Workspace.Browse.Messages);
            Assert.Equal(active.MessageId, dead.MessageId);
            Assert.NotEqual(active.Key, dead.Key);
            Assert.False(active.IsDeadLetter);
            Assert.True(dead.IsDeadLetter);
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Message_body_and_properties_survive_browse_projection() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync();
        try
        {
            await fixture.SendQueueAsync("rich-message", "{\"amount\":19.95}", "application/json", "correlation-a");
            await fixture.OpenQueueAsync();
            MessageRow row = Assert.Single(fixture.Workspace.Browse.Messages);
            Assert.Equal("{\"amount\":19.95}", row.Delivery.Message.Body);
            Assert.Equal("correlation-a", row.CorrelationId);
            Assert.Equal("application/json", row.Delivery.Message.ContentType);
        }
        finally { await fixture.CleanupAsync(); }
    });

    [UiNavigationSmokeFact(Timeout = 60_000)]
    [Trait("TestCategory", "DailyAudit")]
    public Task Expired_messages_remain_inspectable_with_expiry_metadata_or_stale_marker_after_refresh() => DailyAuditDispatcher.RunAsync(async () =>
    {
        await using var fixture = await AuditFixture.CreateAsync(defaultTtl: TimeSpan.FromSeconds(5));
        try
        {
            await fixture.SendQueueAsync("short-lived", "expires");
            await fixture.OpenQueueAsync();
            Assert.Single(fixture.Workspace.Browse.Messages);
            await Task.Delay(TimeSpan.FromSeconds(6), fixture.OperationToken);
            Assert.False(await fixture.TryReceiveQueueMessageAsync(),
                "An expired message must not be available for normal receive.");
            await fixture.Workspace.Browse.RefreshAsync();
            // Diagnostic Peek may retain expired entries until broker garbage collection.
            // A retained entry must preserve its expired timestamp or be marked unobserved.
            Assert.All(fixture.Workspace.Browse.Messages.Where(row => row.MessageId == "short-lived"), row =>
                Assert.True(row.ObservationDetail.Length > 0 || row.Delivery.Message.ExpiresAt <= DateTimeOffset.UtcNow));
        }
        finally { await fixture.CleanupAsync(); }
    });

    private sealed class AuditFixture : IAsyncDisposable
    {
        private readonly string profilePath;
        private readonly ServiceBusAdministrationClient admin;
        private readonly DirectServiceBusClientFactory setupFactory;
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(55));
        private bool cleaned;
        private bool queueCreated;
        private bool secondQueueCreated;
        private bool topicCreated;

        private AuditFixture(string runId, int queuePageSize, int topicPageSize, TimeSpan? defaultTtl)
        {
            profileId = $"daily-audit-{runId}";
            QueueName = $"daily-audit-q-{runId}";
            SecondQueueName = $"daily-audit-q2-{runId}";
            TopicName = $"daily-audit-t-{runId}";
            profilePath = Path.Combine(Path.GetTempPath(), "sbe-daily-audit", runId, "preferences.json");
            admin = new ServiceBusAdministrationClient(ServiceBusUiSmokeEnvironment.AdminConnectionString);
            setupFactory = new DirectServiceBusClientFactory();
            Profile = new InvestigationProfile(profileId, new ConnectionProfile(
                "Daily audit",
                ServiceBusUiSmokeEnvironment.RuntimeConnectionString,
                ServiceBusUiSmokeEnvironment.AdminConnectionString));
            DefaultTtl = defaultTtl;
            QueuePageSize = queuePageSize;
            TopicPageSize = topicPageSize;
        }

        private readonly string profileId;
        public string QueueName { get; }
        public string SecondQueueName { get; }
        public string TopicName { get; }
        public string SubscriptionOne => "first";
        public string SubscriptionTwo => "second";
        public InvestigationProfile Profile { get; }
        public InvestigationWorkspace Workspace { get; private set; } = null!;
        private CancellationToken Token => deadline.Token;
        public CancellationToken OperationToken => Token;
        public EntityNode? QueueNode => Workspace.Browse.AllEntities().FirstOrDefault(node => node.Path == QueueName);
        private EntityNode? SecondQueueNode => Workspace.Browse.AllEntities().FirstOrDefault(node => node.Path == SecondQueueName);
        private EntityNode? TopicNode => Workspace.Browse.AllEntities().FirstOrDefault(node => node.Path == TopicName);
        public EntityNode? TopicNodeForAudit => TopicNode;
        private readonly int QueuePageSize;
        private readonly int TopicPageSize;
        private readonly TimeSpan? DefaultTtl;

        public static async Task<AuditFixture> CreateAsync(int queuePageSize = 50, int topicPageSize = 50, TimeSpan? defaultTtl = null)
        {
            var fixture = new AuditFixture(Guid.NewGuid().ToString("N"), queuePageSize, topicPageSize, defaultTtl);
            try
            {
                await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(fixture.Token);
                if (defaultTtl is { } ttl)
                    await fixture.admin.CreateQueueAsync(new CreateQueueOptions(fixture.QueueName) { DefaultMessageTimeToLive = ttl }, fixture.Token);
                else
                    await fixture.admin.CreateQueueAsync(fixture.QueueName, fixture.Token);
                fixture.queueCreated = true;
                await fixture.ConnectWorkspaceAsync();
                return fixture;
            }
            catch
            {
                await fixture.CleanupAsync();
                throw;
            }
        }

        public static async Task<AuditFixture> CreateTwoQueuesAsync()
        {
            var fixture = new AuditFixture(Guid.NewGuid().ToString("N"), 50, 50, null);
            try
            {
                await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(fixture.Token);
                await fixture.admin.CreateQueueAsync(fixture.QueueName, fixture.Token);
                fixture.queueCreated = true;
                await fixture.admin.CreateQueueAsync(fixture.SecondQueueName, fixture.Token);
                fixture.secondQueueCreated = true;
                await fixture.ConnectWorkspaceAsync();
                return fixture;
            }
            catch
            {
                await fixture.CleanupAsync();
                throw;
            }
        }

        public static async Task<AuditFixture> CreateTopicAsync(int topicPageSize = 50)
        {
            var fixture = new AuditFixture(Guid.NewGuid().ToString("N"), 50, topicPageSize, null);
            try
            {
                await ServiceBusUiSmokeEnvironment.WaitUntilReadyAsync(fixture.Token);
                await fixture.admin.CreateTopicAsync(fixture.TopicName, fixture.Token);
                fixture.topicCreated = true;
                await fixture.admin.CreateSubscriptionAsync(fixture.TopicName, fixture.SubscriptionOne, fixture.Token);
                await fixture.admin.CreateSubscriptionAsync(fixture.TopicName, fixture.SubscriptionTwo, fixture.Token);
                await fixture.ConnectWorkspaceAsync();
                return fixture;
            }
            catch
            {
                await fixture.CleanupAsync();
                throw;
            }
        }

        private async Task ConnectWorkspaceAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            var preferences = new WorkspacePreferences
            {
                Profiles = [Profile],
                SelectedProfileId = profileId,
                QueuePageSize = QueuePageSize,
                TopicPageSize = TopicPageSize,
                SubscriptionPageSize = TopicPageSize,
                SelectedEntityPath = QueueName
            };
            var store = new ProtectedWorkspacePreferencesStore(profilePath);
            await store.SaveAsync(preferences, Token);
            var connectionWorkflow = new BrokerConnectionWorkflow(
                () => new DirectServiceBusClientFactory(),
                factory => new InvestigationEntityBrowser(factory),
                factory => new ServiceBusMessageService(factory));
            Workspace = new InvestigationWorkspace(store, connectionWorkflow);
            await Workspace.InitializeAsync();
            await Workspace.ConnectAsync();
            await setupFactory.ConnectAsync(Profile.Connection, Token);
        }

        public async Task OpenQueueAsync(bool deadLetter = false)
        {
            EntityNode node = QueueNode ?? throw new InvalidOperationException($"Queue {QueueName} was not discovered.");
            await Workspace.Browse.SelectAsync(node, deadLetter);
        }

        public async Task OpenSecondQueueAsync()
        {
            EntityNode node = SecondQueueNode ?? throw new InvalidOperationException($"Queue {SecondQueueName} was not discovered.");
            await Workspace.Browse.SelectAsync(node, false);
        }

        public async Task OpenTopicAsync()
        {
            EntityNode node = TopicNode ?? throw new InvalidOperationException($"Topic {TopicName} was not discovered.");
            await Workspace.Browse.SelectAsync(node, false);
        }

        public async Task OpenSubscriptionAsync(string subscriptionName)
        {
            EntityNode node = SubscriptionNodeForAudit(subscriptionName);
            await Workspace.Browse.SelectAsync(node, false);
        }

        public EntityNode SubscriptionNodeForAudit(string subscriptionName) =>
            Workspace.Browse.AllEntities().FirstOrDefault(node => node.Path == $"{TopicName}/{subscriptionName}")
            ?? throw new InvalidOperationException($"Subscription {TopicName}/{subscriptionName} was not discovered.");

        public async Task SendQueueAsync(string messageId, string body, string? contentType = null, string? correlationId = null, string? queue = null)
        {
            await using ServiceBusSender sender = setupFactory.RuntimeClient.CreateSender(queue ?? QueueName);
            await sender.SendMessageAsync(new ServiceBusMessage(body)
            {
                MessageId = messageId,
                ContentType = contentType,
                TimeToLive = DefaultTtl ?? TimeSpan.FromHours(1),
                CorrelationId = correlationId
            }, Token);
        }

        public async Task SendTopicAsync(string messageId)
        {
            await using ServiceBusSender sender = setupFactory.RuntimeClient.CreateSender(TopicName);
            await sender.SendMessageAsync(new ServiceBusMessage($"body-{messageId}") { MessageId = messageId }, Token);
            await WaitForTopicDeliveryAsync(SubscriptionOne, messageId);
            await WaitForTopicDeliveryAsync(SubscriptionTwo, messageId);
        }

        private async Task WaitForTopicDeliveryAsync(string subscriptionName, string messageId)
        {
            var source = new EntityAddress(EntityKind.Subscription, subscriptionName, TopicName);
            var service = new ServiceBusMessageService(setupFactory);
            DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                IReadOnlyList<ExplorerMessage> messages = await service.PeekMessagesAsync(
                    source, MessageBucket.Active, 100, null, Token);
                if (messages.Any(message => message.MessageId == messageId)) return;
                await Task.Delay(TimeSpan.FromMilliseconds(250), Token);
            }

            throw new TimeoutException($"Topic delivery {messageId} did not reach {TopicName}/{subscriptionName} within 15 seconds.");
        }

        public async Task MoveOneQueueMessageToDeadLetterAsync()
        {
            await using ServiceBusReceiver receiver = setupFactory.RuntimeClient.CreateReceiver(QueueName);
            ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), Token);
            Assert.NotNull(message);
            await receiver.DeadLetterMessageAsync(message, cancellationToken: Token);
        }

        public async Task ReceiveAndCompleteQueueMessageAsync()
        {
            await using ServiceBusReceiver receiver = setupFactory.RuntimeClient.CreateReceiver(QueueName);
            ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), Token);
            Assert.NotNull(message);
            await receiver.CompleteMessageAsync(message, Token);
        }

        public async Task<bool> TryReceiveQueueMessageAsync()
        {
            await using ServiceBusReceiver receiver = setupFactory.RuntimeClient.CreateReceiver(QueueName);
            ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2), Token);
            if (message is null) return false;
            await receiver.AbandonMessageAsync(message, cancellationToken: Token);
            return true;
        }

        public async Task<long> PeekCountAsync(EntityAddress address, MessageBucket bucket)
        {
            long? cursor = null;
            long count = 0;
            while (true)
            {
                IReadOnlyList<ExplorerMessage> page = await new ServiceBusMessageService(setupFactory)
                    .PeekMessagesAsync(address, bucket, 100, cursor, Token);
                if (page.Count == 0) return count;
                count += page.Count;
                cursor = page.Max(message => message.SequenceNumber) + 1;
            }
        }

        public async Task CleanupAsync()
        {
            if (cleaned) return;
            cleaned = true;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (Workspace is not null) await Workspace.DisposeAsync();
            await setupFactory.DisposeAsync();
            if (queueCreated) await admin.DeleteQueueAsync(QueueName, cleanup.Token);
            if (secondQueueCreated) await admin.DeleteQueueAsync(SecondQueueName, cleanup.Token);
            if (topicCreated) await admin.DeleteTopicAsync(TopicName, cleanup.Token);
            string profileDirectory = Path.GetDirectoryName(profilePath)!;
            if (Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory, true);
            deadline.Dispose();
        }

        public async ValueTask DisposeAsync() => await CleanupAsync();
    }
}
