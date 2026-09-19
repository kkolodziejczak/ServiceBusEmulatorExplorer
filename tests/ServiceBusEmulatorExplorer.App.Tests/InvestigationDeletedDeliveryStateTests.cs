using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationDeletedDeliveryStateTests
{
    [Fact]
    public async Task Browse_removes_only_confirmed_identity_and_preserves_other_checked_focus()
    {
        var messages = new Messages();
        var browse = Browse(messages);
        await browse.SelectAsync(browse.AllEntities().Single(node => node.Address == Address), true);
        var deleted = browse.Messages[0];
        var retained = browse.Messages[1];
        deleted.IsSelected = retained.IsSelected = true;
        browse.FocusedMessage = retained;

        browse.ForgetDeleted(new HashSet<DeliveryIdentity> { deleted.Key });
        await browse.OpenKnownAsync(deleted);

        Assert.Same(retained, Assert.Single(browse.Messages));
        Assert.True(retained.IsSelected);
        Assert.Same(retained, browse.FocusedMessage);
    }

    [Fact]
    public async Task Held_browse_refresh_cannot_resurrect_confirmed_deleted_delivery()
    {
        var messages = new Messages();
        var browse = Browse(messages);
        await browse.SelectAsync(browse.AllEntities().Single(node => node.Address == Address), true);
        var deleted = browse.Messages[0];
        var retained = browse.Messages[1];
        retained.IsSelected = true;
        browse.FocusedMessage = retained;
        messages.HoldNext = true;
        Task refresh = browse.RefreshAsync();
        await messages.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        browse.ForgetDeleted(new HashSet<DeliveryIdentity> { deleted.Key });
        messages.Release.SetResult(true);
        await refresh;

        Assert.Same(retained, Assert.Single(browse.Messages));
        Assert.Same(retained, browse.FocusedMessage);
        Assert.True(retained.IsSelected);
    }

    [Fact]
    public async Task Held_search_response_cannot_publish_confirmed_deleted_delivery()
    {
        var messages = new Messages { HoldNext = true };
        var search = new MessageSearchWorkflow();
        search.SetSession(Session(messages), 1);
        Task scan = search.StartAsync("*", true);
        await messages.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        search.ForgetDeleted(new HashSet<DeliveryIdentity> { Delivery(1).Identity });
        messages.Release.SetResult(true);
        await scan;

        Assert.Equal(2, Assert.Single(search.Messages).Key.SequenceNumber);
        Assert.Contains("1 matches", search.CountSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_removes_exact_delivery_without_clearing_unrelated_selection_and_focus()
    {
        var search = new MessageSearchWorkflow();
        search.SetSession(Session(new Messages()), 1);
        await search.StartAsync("*", true);
        var deleted = search.Messages[0];
        var retained = search.Messages[1];
        retained.IsSelected = true;
        search.FocusedMessage = retained;

        search.ForgetDeleted(new HashSet<DeliveryIdentity> { deleted.Key });

        Assert.Same(retained, Assert.Single(search.Messages));
        Assert.Same(retained, search.FocusedMessage);
        Assert.Equal(1, search.SelectedCount);
    }

    [Fact]
    public void Inspector_forgets_deleted_draft_but_keeps_unrelated_document_and_undo_history()
    {
        var inspector = new DeliveryInspector();
        var deleted = Delivery(1);
        var retained = Delivery(2);
        inspector.Select(deleted);
        inspector.Document.Text = "{\"deletedDraft\":true}";
        inspector.Select(retained);
        inspector.Document.Replace(0, inspector.Document.TextLength, "{\"retainedDraft\":true}");
        var document = inspector.Document;

        inspector.ForgetDeleted(new HashSet<DeliveryIdentity> { deleted.Identity });

        Assert.False(inspector.HasDraft(deleted.Identity));
        Assert.True(inspector.HasDraft(retained.Identity));
        Assert.Same(document, inspector.Document);
        Assert.Equal(retained, inspector.Current);
        Assert.True(document.UndoStack.CanUndo);
        inspector.ForgetDeleted(new HashSet<DeliveryIdentity> { retained.Identity });
        Assert.Null(inspector.Current);
        Assert.False(inspector.HasDrafts);
    }

    [Fact]
    public void Watch_removes_exact_delivery_but_preserves_same_sequence_from_other_bucket_source_and_generation()
    {
        var watch = new MessageWatchWorkflow();
        var deleted = Delivery(1);
        var otherBucket = new MessageDelivery(deleted.Identity with { Bucket = MessageBucket.Active }, deleted.Message);
        var otherSource = new MessageDelivery(deleted.Identity with { Source = new EntityAddress(EntityKind.Queue, "other") }, deleted.Message);
        var otherGeneration = new MessageDelivery(deleted.Identity with { ConnectionGeneration = 2 }, deleted.Message);
        foreach (var delivery in new[] { deleted, otherBucket, otherSource, otherGeneration }) watch.PendingArrivals.Add(delivery);

        watch.ForgetDeleted(new HashSet<DeliveryIdentity> { deleted.Identity });

        Assert.Equal(new[] { otherBucket, otherSource, otherGeneration }, watch.PendingArrivals);
    }

    [Fact]
    public void Watch_forgets_old_session_notification_for_same_physical_DLQ_delivery_only()
    {
        var watch = new MessageWatchWorkflow();
        var timestamp = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var confirmed = new MessageDelivery(Delivery(1).Identity with { ConnectionGeneration = 2 },
            Message(1) with { EnqueuedTime = timestamp, RawBody = BinaryData.FromString("original bytes") });
        var oldIdentity = confirmed.Identity with { ConnectionGeneration = 1 };
        var oldNotification = new MessageDelivery(oldIdentity, confirmed.Message);
        var differentBody = new MessageDelivery(oldIdentity,
            confirmed.Message with { RawBody = BinaryData.FromString("different bytes") });
        var differentEnqueued = new MessageDelivery(oldIdentity,
            confirmed.Message with { EnqueuedTime = timestamp.AddSeconds(1) });
        var differentSource = new MessageDelivery(oldIdentity with { Source = new EntityAddress(EntityKind.Queue, "other") }, confirmed.Message);
        var activeNeighbor = new MessageDelivery(oldIdentity with { Bucket = MessageBucket.Active }, confirmed.Message);
        foreach (var delivery in new[] { oldNotification, differentBody, differentEnqueued, differentSource, activeNeighbor })
            watch.PendingArrivals.Add(delivery);

        watch.ForgetDeleted(new MessageDelivery[] { confirmed });

        Assert.Equal(new[] { differentBody, differentEnqueued, differentSource, activeNeighbor }, watch.PendingArrivals);
    }

    private static readonly EntityAddress Address = new(EntityKind.Queue, "orders");

    private static MessageBrowseWorkflow Browse(Messages messages)
    {
        var browse = new MessageBrowseWorkflow();
        browse.SetSession(Session(messages), 1);
        return browse;
    }

    private static BrokerSession Session(Messages messages)
    {
        var entity = new ServiceBusEntityNode(EntityKind.Queue, "orders", null,
            new EntityRuntimeCounts(0, 2, 0, 2), new EntityMetadata("orders", "Active", null, null, null, null, null, null, null));
        var counts = new EntityCountObservation(new(0, CountAvailability.Known), new(2, CountAvailability.Known), new(0, CountAvailability.Known));
        var snapshot = new EntityDiscoverySnapshot([new EntityObservation(entity, counts)], DateTimeOffset.UtcNow, true, []);
        return new(null!, new Browser(snapshot), messages, snapshot, null);
    }

    private static MessageDelivery Delivery(long sequence) => new(new(1, Address, MessageBucket.DeadLetter, sequence), Message(sequence));

    private static ExplorerMessage Message(long sequence) => new("same-case", sequence, "{}", "{}", 2,
        null, null, 0, null, null, null, null, new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private sealed class Browser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public bool HoldNext { get; set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket,
            int take, long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            if (HoldNext)
            {
                HoldNext = false;
                Started.SetResult(true);
                await Release.Task;
            }
            return bucket == MessageBucket.DeadLetter
                ? new[] { Message(1), Message(2) }.Where(message => message.SequenceNumber >= (fromSequenceNumber ?? 0)).Take(take).ToArray()
                : [];
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
