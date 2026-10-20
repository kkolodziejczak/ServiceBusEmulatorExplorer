using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

/// <summary>A connected investigation workspace browsing the dead-letter queue of one queue with three messages.</summary>
internal sealed class WorkspaceFixture : IAsyncDisposable
{
    public static readonly ServiceBusReceivedMessage[] BrokerMessages = Enumerable.Range(1, 3).Select(sequence =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{\"order\":80341}"),
            messageId: "checkout-original-message-" + sequence, sequenceNumber: sequence,
            enqueuedTime: new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), contentType: "application/json")).ToArray();

    private WorkspaceFixture()
    {
        Workspace = new InvestigationWorkspace(Store,
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => new Messages()),
            _ => Sender, (_, _) => Receiver);
    }

    public InvestigationWorkspace Workspace { get; }
    public FakeStore Store { get; } = new();
    public FakeSender Sender { get; } = new();
    public FakeReceiver Receiver { get; } = new();

    public IReadOnlyList<MessageRow> Rows => Workspace.Browse.Messages;

    public static async Task<WorkspaceFixture> OpenAsync()
    {
        var fixture = new WorkspaceFixture();
        await fixture.Workspace.InitializeAsync();
        if (!fixture.Workspace.IsConnected) await fixture.Workspace.ConnectAsync();
        await fixture.Workspace.Browse.SelectAsync(fixture.Workspace.Browse.AllEntities().Single(), deadLetter: true);
        Assert.Equal(3, fixture.Rows.Count);
        return fixture;
    }

    /// <summary>Checks the given rows and focuses and inspects the first of them.</summary>
    public void Check(params int[] indexes)
    {
        foreach (int index in indexes) Rows[index].IsSelected = true;
        Focus(indexes[0]);
    }

    public void Focus(int index)
    {
        Workspace.Browse.FocusedMessage = Rows[index];
        Workspace.Inspector.Select(Rows[index].Delivery);
    }

    public IReadOnlyList<string> Log => Workspace.Activity.Select(entry => entry.Message).ToArray();

    public ValueTask DisposeAsync() => Workspace.DisposeAsync();

    public sealed class FakeStore : IWorkspacePreferencesStore
    {
        public WorkspacePreferences Saved { get; private set; } = new()
        {
            Profiles = [new("profile", ConnectionProfileDefaults.LocalEmulator)],
            SelectedProfileId = "profile", CloseToTray = false, WasConnected = false
        };

        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(Saved));

        public Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            Saved = preferences;
            return Task.CompletedTask;
        }
    }

    public sealed class FakeSender : IReplayCopySender
    {
        public List<ServiceBusMessage> Sends { get; } = [];
        public Func<ServiceBusMessage, Task> OnSend { get; set; } = _ => Task.CompletedTask;

        public Task SendAsync(EntityAddress destination, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            Sends.Add(message);
            return OnSend(message);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class FakeReceiver : IDlqDeleteReceiver
    {
        private bool received;

        public List<long> Completed { get; } = [];
        public Func<ServiceBusReceivedMessage, CancellationToken, Task> Complete { get; set; } = (_, _) => Task.CompletedTask;

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken)
        {
            IReadOnlyList<ServiceBusReceivedMessage> batch = received ? [] : BrokerMessages;
            received = true;
            return Task.FromResult(batch);
        }

        public async Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
        {
            await Complete(message, cancellationToken);
            Completed.Add(message.SequenceNumber);
        }

        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Factory : IServiceBusClientFactory
    {
        public ServiceBusAdministrationClient AdministrationClient => throw new InvalidOperationException();
        public ServiceBusClient RuntimeClient => throw new InvalidOperationException();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Browser : IInvestigationEntityBrowser
    {
        public Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(
            new EntityDiscoverySnapshot([new(new ServiceBusEntityNode(EntityKind.Queue, "orders", null, new(0, 3, 0, 3),
                new("orders", "Active", null, null, null, null, null, null, null)),
                new(new(0, CountAvailability.Known), new(3, CountAvailability.Known), new(0, CountAvailability.Known)))],
                DateTimeOffset.UtcNow, true, []));
    }

    private sealed class Messages : IServiceBusMessageService
    {
        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExplorerMessage>>(
            BrokerMessages.Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                .Take(take).Select(MessageProjection.Create).ToArray());

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}
