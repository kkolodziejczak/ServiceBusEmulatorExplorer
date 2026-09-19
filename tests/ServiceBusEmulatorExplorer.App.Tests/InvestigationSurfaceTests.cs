using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationSurfaceTests
{
    [Fact]
    public async Task Search_switch_and_clear_restore_browse_rows_focus_check_and_inspector_draft()
    {
        EntityAddress address = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, Message("order-1", 1));
        await using var workspace = CreateWorkspace(snapshot, messages);
        workspace.Browse.SetSession(CreateSession(snapshot, messages), 1);
        workspace.Search.SetSession(CreateSession(snapshot, messages), 1);
        EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Name == "orders");
        await workspace.Browse.SelectAsync(queue, deadLetter: false);

        MessageRow browseRow = Assert.Single(workspace.Browse.Messages);
        browseRow.IsSelected = true;
        workspace.Browse.FocusedMessage = browseRow;
        workspace.Inspector.Select(browseRow.Delivery);
        workspace.Inspector.Document.Insert(0, " ");
        string draft = workspace.Inspector.Document.Text;

        await workspace.Search.StartAsync("*", defaultMessageId: true);
        Assert.True(workspace.Surface.IsSearch);
        Assert.NotSame(browseRow, workspace.Surface.Messages.Single());

        workspace.Search.Clear();

        Assert.False(workspace.Surface.IsSearch);
        Assert.Same(browseRow, workspace.Surface.Messages.Single());
        Assert.Same(browseRow, workspace.Surface.FocusedMessage);
        Assert.True(browseRow.IsSelected);
        Assert.True(workspace.Inspector.HasDrafts);
        Assert.Equal(draft, workspace.Inspector.Document.Text);
    }

    [Fact]
    public async Task Changing_session_clears_search_and_returns_surface_to_empty_browse_state()
    {
        EntityAddress address = new(EntityKind.Queue, "orders");
        EntityDiscoverySnapshot snapshot = Snapshot(Queue("orders"));
        var messages = new FakeMessages();
        messages.Set(address, Message("order-1", 1));
        await using var workspace = CreateWorkspace(snapshot, messages);
        BrokerSession session = CreateSession(snapshot, messages);
        workspace.Browse.SetSession(session, 1);
        workspace.Search.SetSession(session, 1);
        EntityNode queue = workspace.Browse.AllEntities().Single(node => node.Name == "orders");
        await workspace.Browse.SelectAsync(queue, deadLetter: false);
        await workspace.Search.StartAsync("*", defaultMessageId: true);
        Assert.True(workspace.Surface.IsSearch);

        workspace.Browse.SetSession(null, 2);
        workspace.Search.SetSession(null, 2);

        Assert.False(workspace.Search.IsActive);
        Assert.Empty(workspace.Search.Messages);
        Assert.Empty(workspace.Search.Roots);
        Assert.False(workspace.Surface.IsSearch);
        Assert.Empty(workspace.Surface.Messages);
        Assert.Null(workspace.Surface.FocusedMessage);
    }

    private static InvestigationWorkspace CreateWorkspace(
        EntityDiscoverySnapshot snapshot,
        FakeMessages messages)
    {
        var connections = new BrokerConnectionWorkflow(
            () => new FakeFactory(),
            _ => new FakeBrowser(snapshot),
            _ => messages);
        return new InvestigationWorkspace(new FakeStore(), connections);
    }

    private static BrokerSession CreateSession(EntityDiscoverySnapshot snapshot, FakeMessages messages)
        => new(new FakeFactory(), new FakeBrowser(snapshot), messages, snapshot, null);

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
        => new(EntityKind.Queue, name, null, new(0, 0, 0, 0), Metadata(name));

    private static EntityMetadata Metadata(string path)
        => new(path, "Active", null, null, null, null, null, null, null);

    private static ExplorerMessage Message(string id, long sequence)
        => new(id, sequence, id, id, id.Length, null, null, 0, null, null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());

    private sealed class FakeBrowser(EntityDiscoverySnapshot snapshot) : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
            => Task.FromResult(snapshot);
    }

    private sealed class FakeMessages : IServiceBusMessageService
    {
        private readonly Dictionary<(EntityAddress Address, MessageBucket Bucket), IReadOnlyList<ExplorerMessage>> messages = [];

        public void Set(EntityAddress address, params ExplorerMessage[] values)
        {
            messages[(address, MessageBucket.Active)] = values;
        }

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<ExplorerMessage> values = messages.TryGetValue((address, bucket), out IReadOnlyList<ExplorerMessage>? found)
                ? found
                : [];
            return Task.FromResult<IReadOnlyList<ExplorerMessage>>(values
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber.Value)
                .Take(take)
                .ToList());
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeFactory : IServiceBusClientFactory
    {
        public Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient AdministrationClient => null!;
        public Azure.Messaging.ServiceBus.ServiceBusClient RuntimeClient => null!;

        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeStore : IWorkspacePreferencesStore
    {
        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult(new PreferencesLoadResult(new WorkspacePreferences()));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
