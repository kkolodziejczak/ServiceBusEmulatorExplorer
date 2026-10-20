using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

/// <summary>A connected investigation workspace browsing one queue whose three messages are all dead-lettered.</summary>
internal sealed class WorkspaceFixture : IAsyncDisposable
{
    public static readonly ServiceBusReceivedMessage[] BrokerMessages = Enumerable.Range(1, 3).Select(sequence =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("{\"order\":80341}"),
            messageId: "checkout-original-message-" + sequence, sequenceNumber: sequence,
            correlationId: sequence == 1 ? "order-80341" : null,
            enqueuedTime: new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), contentType: "application/json")).ToArray();

    private readonly List<TaskCompletionSource> gates = [];

    private WorkspaceFixture()
    {
        Workspace = new InvestigationWorkspace(Store,
            new BrokerConnectionWorkflow(() => new Factory(), _ => new Browser(), _ => Peeks),
            _ => CreateSender(), (_, _) => Receiver);
        CreateSender = () => Sender;
    }

    public InvestigationWorkspace Workspace { get; }
    public FakeStore Store { get; } = new();
    public FakeSender Sender { get; } = new();

    /// <summary>Creates the replay sender for each send; <see cref="Sender"/> by default.</summary>
    public Func<IReplayCopySender> CreateSender { get; set; }
    public FakeReceiver Receiver { get; } = new();
    public FakeMessages Peeks { get; } = new();

    public IReadOnlyList<MessageRow> Rows => Workspace.Browse.Messages;

    public static async Task<WorkspaceFixture> OpenAsync()
    {
        var fixture = new WorkspaceFixture();
        await fixture.Workspace.InitializeAsync();
        if (!fixture.Workspace.IsConnected) await fixture.Workspace.ConnectAsync();
        await fixture.Workspace.Browse.SelectAsync(fixture.Queue, deadLetter: true);
        Assert.Equal(3, fixture.Rows.Count);
        return fixture;
    }

    public EntityNode Queue => Workspace.Browse.AllEntities().Single();

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

    /// <summary>
    /// A gate a fake broker or store call can wait on. Disposing the fixture opens every gate, so a test that fails
    /// before opening its gate cannot hang while the workspace waits for the held operation.
    /// </summary>
    public TaskCompletionSource Gate()
    {
        var gate = new TaskCompletionSource();
        gates.Add(gate);
        return gate;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var gate in gates) gate.TrySetResult();
        return Workspace.DisposeAsync();
    }

    public sealed class FakeStore : IWorkspacePreferencesStore
    {
        public WorkspacePreferences Saved { get; private set; } = new()
        {
            Profiles = [new("profile", ConnectionProfileDefaults.LocalEmulator)],
            SelectedProfileId = "profile", CloseToTray = false, WasConnected = false
        };

        public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreferencesLoadResult(Saved));

        /// <summary>Runs before each save; a save waits for it and fails when it throws.</summary>
        public Func<Task> BeforeSave { get; set; } = () => Task.CompletedTask;

        public async Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
        {
            await BeforeSave();
            Saved = preferences;
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

        public Func<ServiceBusReceivedMessage, Task> Abandon { get; set; } = _ => Task.CompletedTask;

        public Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Abandon(message);
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

    public sealed class FakeMessages : IServiceBusMessageService
    {
        /// <summary>Runs before each peek; a peek waits for it.</summary>
        public Func<Task> BeforePeek { get; set; } = () => Task.CompletedTask;

        public async Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(EntityAddress address, MessageBucket bucket, int take,
            long? fromSequenceNumber, CancellationToken cancellationToken)
        {
            await BeforePeek().WaitAsync(cancellationToken);
            return BrokerMessages.Where(message => bucket == MessageBucket.DeadLetter)
                .Where(message => fromSequenceNumber is null || message.SequenceNumber >= fromSequenceNumber)
                .Take(take).Select(MessageProjection.Create).ToArray();
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}
