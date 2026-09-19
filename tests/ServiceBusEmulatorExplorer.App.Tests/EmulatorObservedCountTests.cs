using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class EmulatorObservedCountTests
{
    [Fact]
    public async Task Browse_counts_only_fetched_deliveries_and_load_more_completes_the_observation()
    {
        var fixture = new Fixture();
        fixture.Set(fixture.Queue, MessageBucket.Active, 1, 2, 3);
        fixture.Workflow.SetPreferences(new WorkspacePreferences { QueuePageSize = 2 });
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        Assert.Equal("2*", fixture.Node().DisplayMessageCount);
        Assert.Contains("partial scan", fixture.Node().ActiveCountDetail);
        Assert.Contains("observed", fixture.Workflow.CountSummary);
        Assert.Equal(1, fixture.Messages.Calls);
        await fixture.Workflow.LoadMoreAsync();
        Assert.Equal("3*", fixture.Node().DisplayMessageCount);
        Assert.Contains("scan complete", fixture.Node().ActiveCountDetail);
        Assert.Contains("UTC", fixture.Node().ActiveCountDetail);
        Assert.Equal("—", fixture.Node().DisplayScheduledCount);
        Assert.Equal("—", fixture.Node().DisplayDlqCount);
    }

    [Fact]
    public async Task Later_page_reobserves_a_retained_row_without_losing_it_from_the_count()
    {
        var fixture = new Fixture();
        fixture.Workflow.SetPreferences(new WorkspacePreferences { QueuePageSize = 2 });
        fixture.Set(fixture.Queue, MessageBucket.Active, 1, 3);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        fixture.Workflow.Messages.Single(row => row.Key.SequenceNumber == 3).IsSelected = true;
        fixture.Set(fixture.Queue, MessageBucket.Active, 1, 2, 3);
        await fixture.Workflow.RefreshAsync();
        Assert.Equal("2*", fixture.Node().DisplayMessageCount);
        await fixture.Workflow.LoadMoreAsync();
        Assert.Equal("3*", fixture.Node().DisplayMessageCount);
        Assert.All(fixture.Workflow.Messages, row => Assert.Empty(row.ObservationDetail));
    }

    [Fact]
    public async Task Removing_all_subscriptions_invalidates_previous_topic_aggregate()
    {
        var fixture = new Fixture(topic: true);
        fixture.Set(new EntityAddress(EntityKind.Subscription, "first", "events"), MessageBucket.Active, 1);
        await fixture.Workflow.SelectAsync(fixture.Node("events"), false);
        fixture.Workflow.ApplySnapshot(fixture.Session.Snapshot with
        {
            Entities = fixture.Session.Snapshot.Entities.Where(item => item.Entity.Kind == EntityKind.Topic).ToArray()
        });
        Assert.Equal("—", fixture.Node("events").DisplayMessageCount);
        await fixture.Workflow.SelectAsync(fixture.Node("events"), false);
        Assert.Equal("0*", fixture.Node("events").DisplayMessageCount);
    }

    [Fact]
    public async Task Empty_topic_observation_survives_entity_tree_refresh()
    {
        var fixture = new Fixture(topic: true);
        var snapshot = fixture.Session.Snapshot with
        {
            Entities = fixture.Session.Snapshot.Entities.Where(item => item.Entity.Kind == EntityKind.Topic).ToArray()
        };
        fixture.Workflow.ApplySnapshot(snapshot);
        await fixture.Workflow.SelectAsync(fixture.Node("events"), false);
        Assert.Equal("0*", fixture.Node("events").DisplayMessageCount);
        fixture.Workflow.ApplySnapshot(snapshot);
        Assert.Equal("0*", fixture.Node("events").DisplayMessageCount);
    }

    [Fact]
    public async Task Empty_successful_scan_is_zero_observed_and_does_not_populate_unvisited_bucket()
    {
        var fixture = new Fixture();
        Assert.Equal("—", fixture.Node().DisplayMessageCount);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        Assert.Equal("0*", fixture.Node().DisplayMessageCount);
        Assert.Contains("scan complete", fixture.Node().ActiveCountDetail);
        Assert.Equal("—", fixture.Node().DisplayDlqCount);
    }

    [Fact]
    public async Task Refresh_replaces_observation_and_excludes_retained_selected_rows()
    {
        var fixture = new Fixture();
        fixture.Set(fixture.Queue, MessageBucket.Active, 1, 2);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        fixture.Workflow.Messages[0].IsSelected = true;
        fixture.Set(fixture.Queue, MessageBucket.Active, 2);
        await fixture.Workflow.RefreshAsync();
        Assert.Equal(2, fixture.Workflow.Messages.Count);
        Assert.Equal("1*", fixture.Node().DisplayMessageCount);
        Assert.Contains(fixture.Workflow.Messages, row => row.ObservationDetail.Length > 0);
    }

    [Fact]
    public async Task Buckets_are_independent_and_reconnect_discards_previous_observations()
    {
        var fixture = new Fixture();
        fixture.Set(fixture.Queue, MessageBucket.Active, 1, 2);
        fixture.Set(fixture.Queue, MessageBucket.DeadLetter, 1);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        await fixture.Workflow.SelectAsync(fixture.Node(), true);
        Assert.Equal("2*", fixture.Node().DisplayMessageCount);
        Assert.Equal("1*", fixture.Node().DisplayDlqCount);
        fixture.Workflow.SetSession(fixture.Session, 2);
        Assert.Equal("—", fixture.Node().DisplayMessageCount);
        Assert.Equal("—", fixture.Node().DisplayDlqCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_canceled_refresh_preserves_last_successful_observation(bool canceled)
    {
        var fixture = new Fixture();
        fixture.Set(fixture.Queue, MessageBucket.Active, 1);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        string detail = fixture.Node().ActiveCountDetail;
        fixture.Messages.Failure = canceled ? new OperationCanceledException() : new InvalidOperationException("unavailable");
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Workflow.RefreshAsync());
        Assert.Equal("1*", fixture.Node().DisplayMessageCount);
        Assert.Equal(detail, fixture.Node().ActiveCountDetail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cloud_profiles_never_substitute_observed_counts(bool known)
    {
        var fixture = new Fixture(emulator: false, known: known);
        fixture.Set(fixture.Queue, MessageBucket.Active, 1, 2);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        Assert.Equal(known ? "30" : "—", fixture.Node().DisplayMessageCount);
        Assert.DoesNotContain("observed", fixture.Workflow.CountSummary);
    }

    [Fact]
    public async Task Topic_counts_subscription_deliveries_even_with_identical_message_ids_and_sequence_numbers()
    {
        var fixture = new Fixture(topic: true);
        var first = new EntityAddress(EntityKind.Subscription, "first", "events");
        var second = new EntityAddress(EntityKind.Subscription, "second", "events");
        fixture.Set(first, MessageBucket.Active, 1);
        fixture.Set(second, MessageBucket.Active, 1);
        fixture.Workflow.SetPreferences(new WorkspacePreferences { TopicPageSize = 1 });
        await fixture.Workflow.SelectAsync(fixture.Node("events"), false);
        Assert.Equal("1*", fixture.Node("events").DisplayMessageCount);
        Assert.Equal("—", fixture.Node("second").DisplayMessageCount);
        Assert.Contains("partial scan", fixture.Node("events").ActiveCountDetail);
        await fixture.Workflow.LoadMoreAsync();
        await fixture.Workflow.LoadMoreAsync();
        Assert.Equal("2*", fixture.Node("events").DisplayMessageCount);
        Assert.Equal("1*", fixture.Node("first").DisplayMessageCount);
        Assert.Equal("1*", fixture.Node("second").DisplayMessageCount);
        Assert.Contains("scan complete", fixture.Node("events").ActiveCountDetail);
    }

    [Fact]
    public async Task Confirmed_delete_updates_cached_bucket_even_when_another_bucket_is_open()
    {
        var fixture = new Fixture();
        fixture.Set(fixture.Queue, MessageBucket.DeadLetter, 1, 2);
        await fixture.Workflow.SelectAsync(fixture.Node(), true);
        var deleted = fixture.Workflow.Messages[0].Key;
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        fixture.Workflow.ForgetDeleted(new HashSet<DeliveryIdentity> { deleted });
        Assert.Equal("1*", fixture.Node().DisplayDlqCount);
    }

    [Fact]
    public async Task Removed_and_recreated_entity_does_not_inherit_previous_counts()
    {
        var fixture = new Fixture();
        fixture.Set(fixture.Queue, MessageBucket.Active, 1);
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        fixture.Workflow.ApplySnapshot(new EntityDiscoverySnapshot([], DateTimeOffset.UtcNow, true, []));
        fixture.Workflow.ApplySnapshot(fixture.Session.Snapshot);
        Assert.Equal("—", fixture.Node().DisplayMessageCount);
    }

    [Fact]
    public async Task Incomplete_discovery_does_not_present_missing_entity_observation_as_current()
    {
        var fixture = new Fixture();
        await fixture.Workflow.SelectAsync(fixture.Node(), false);
        fixture.Workflow.ApplySnapshot(new EntityDiscoverySnapshot([], DateTimeOffset.UtcNow, false, ["Listing failed"]));
        Assert.Equal("—", fixture.Node().DisplayMessageCount);
        Assert.Contains("incomplete", fixture.Node().ActiveCountDetail);
    }

    private sealed class Fixture
    {
        public EntityAddress Queue { get; } = new(EntityKind.Queue, "orders");
        public FakeMessages Messages { get; } = new();
        public MessageBrowseWorkflow Workflow { get; } = new();
        public BrokerSession Session { get; }
        public Fixture(bool emulator = true, bool known = false, bool topic = false)
        {
            var entities = topic
                ? new[] { Entity(EntityKind.Topic, "events", known), Entity(EntityKind.Subscription, "first", known, "events"), Entity(EntityKind.Subscription, "second", known, "events") }
                : new[] { Entity(EntityKind.Queue, "orders", known) };
            var snapshot = new EntityDiscoverySnapshot(entities, DateTimeOffset.UtcNow, true, []);
            Session = new(new FakeFactory(!emulator), new FakeBrowser(snapshot), Messages, snapshot, null);
            Workflow.SetSession(Session, 1);
        }
        public EntityNode Node(string name = "orders") => Workflow.AllEntities().Single(node => node.Name == name);
        public void Set(EntityAddress address, MessageBucket bucket, params long[] sequences) => Messages.Values[(address, bucket)] = sequences.Select(sequence =>
            new ExplorerMessage("same-id", sequence, "{}", "{}", 2, null, null, 0, null, null, null, null,
                new Dictionary<string, object?>(), new Dictionary<string, object?>())).ToArray();
        private static EntityObservation Entity(EntityKind kind, string name, bool known, string? topic = null)
        {
            var count = new CountObservation(known ? 30 : null, known ? CountAvailability.Known : CountAvailability.Unavailable);
            return new(new DiscoveredEntity(kind, name, topic, new EntityMetadata(name, "Active", null, null, null, null, null, null, null)), new(count, count, count));
        }
    }
    private sealed class FakeFactory(bool supportsCounts) : IServiceBusClientFactory
    {
        public bool SupportsRuntimeCounts => supportsCounts;
        public ServiceBusAdministrationClient AdministrationClient => null!;
        public ServiceBusClient RuntimeClient => null!;
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
    private sealed class FakeMessages : IServiceBusMessageService
    {
        public Dictionary<(EntityAddress, MessageBucket), ExplorerMessage[]> Values { get; } = [];
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(Values.GetValueOrDefault((address, bucket), [])
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber).Take(take).ToArray());
        }
        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
