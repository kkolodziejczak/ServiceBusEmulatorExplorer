using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task LoadProfilesAsync_applies_first_saved_profile()
    {
        var profile = new ConnectionProfile(
            "Saved",
            "Endpoint=sb://runtime;UseDevelopmentEmulator=true;",
            "Endpoint=sb://admin;UseDevelopmentEmulator=true;");
        var viewModel = CreateViewModel(store: new FakeProfileStore([profile]));

        await viewModel.LoadProfilesAsync(CancellationToken.None);

        Assert.Equal("Saved", viewModel.ProfileName);
        Assert.Equal(profile.RuntimeConnectionString, viewModel.RuntimeConnectionString);
        Assert.Equal(profile.AdministrationConnectionString, viewModel.AdministrationConnectionString);
    }

    [Fact]
    public async Task ConnectCommand_saves_profile_connects_factory_and_enables_disconnect_refresh()
    {
        var store = new FakeProfileStore([]);
        var factory = new FakeClientFactory();
        var administrationService = new FakeAdministrationService([]);
        var viewModel = CreateViewModel(store, factory, administrationService);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Connected to Local emulator", viewModel.ConnectionStatus);
        Assert.True(factory.ConnectCalled);
        Assert.True(administrationService.GetEntityTreeCalled);
        Assert.Single(store.SavedProfiles);
        Assert.True(viewModel.DisconnectCommand.CanExecute(null));
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadProfilesAsync_falls_back_to_default_profile_when_store_fails()
    {
        var viewModel = CreateViewModel(store: new ThrowingProfileStore());

        await viewModel.LoadProfilesAsync(CancellationToken.None);

        Assert.Equal(ConnectionProfileDefaults.LocalEmulator.Name, viewModel.ProfileName);
        Assert.Contains("Could not load saved connection profiles.", viewModel.OperationLog[0].Message);
    }

    [Fact]
    public async Task ConnectCommand_logs_validation_errors_without_connecting()
    {
        var factory = new FakeClientFactory();
        var viewModel = CreateViewModel(clientFactory: factory);
        viewModel.RuntimeConnectionString = "";

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.False(factory.ConnectCalled);
        Assert.Contains("Runtime connection string is required.", viewModel.OperationLog[0].Message);
    }

    [Fact]
    public void OperationLogEntry_formats_timestamp_as_utc_zulu()
    {
        var timestamp = new DateTimeOffset(2026, 7, 8, 12, 34, 56, 789, TimeSpan.Zero);
        var entry = new OperationLogEntry(timestamp, "Connected.");

        Assert.Equal("2026-07-08T12:34:56.789Z Connected.", entry.DisplayText);
    }

    [Fact]
    public async Task RefreshCommand_groups_entities_and_selection_updates_detail_header()
    {
        var queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 3, deadLetter: 1);
        var topic = CreateEntity(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0);
        var subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 5, deadLetter: 2);
        var viewModel = CreateViewModel(administrationService: new FakeAdministrationService([subscription, queue, topic]));

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Loaded 3 entities.", viewModel.EntityBrowserStatus);
        Assert.Equal("Queues", viewModel.EntityTree[0].DisplayName);
        Assert.Equal("orders", viewModel.EntityTree[0].Children[0].DisplayName);
        Assert.Equal("Topics", viewModel.EntityTree[1].DisplayName);
        Assert.Equal("events", viewModel.EntityTree[1].Children[0].DisplayName);
        Assert.Equal("billing", viewModel.EntityTree[1].Children[0].Children[0].DisplayName);

        viewModel.SelectEntity(viewModel.EntityTree[1].Children[0].Children[0]);

        Assert.Equal("billing", viewModel.SelectedEntityTitle);
        Assert.Equal("Subscription", viewModel.SelectedEntityKind);
        Assert.Equal("events/subscriptions/billing", viewModel.SelectedEntityPath);
        Assert.Equal("Active 5 | DLQ 2 | Scheduled 0 | Total 7", viewModel.SelectedEntityCounts);
        Assert.Contains("Status Active", viewModel.SelectedEntityMetadata);
        Assert.Contains("Created 2026-07-08T10:00:00.000Z", viewModel.SelectedEntityMetadata);
        Assert.Contains("Max deliveries 10", viewModel.SelectedEntityMetadata);
        Assert.Contains("Delivery Max deliveries 10", viewModel.SelectedEntityMetadata);
        Assert.Contains("TTL 14.00:00:00", viewModel.SelectedEntityMetadata);
    }

    [Fact]
    public async Task NamespaceFilter_filters_loaded_entities_and_keeps_matching_subscription_parent()
    {
        var queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 3, deadLetter: 1);
        var topic = CreateEntity(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0);
        var subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 5, deadLetter: 2);
        var viewModel = CreateViewModel(administrationService: new FakeAdministrationService([queue, topic, subscription]));

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.NamespaceFilter = "bill";

        Assert.Empty(viewModel.EntityTree[0].Children);
        Assert.Equal("events", viewModel.EntityTree[1].Children[0].DisplayName);
        Assert.Equal("billing", viewModel.EntityTree[1].Children[0].Children[0].DisplayName);
    }

    [Fact]
    public async Task RefreshCommand_shows_visible_error_when_administration_service_fails()
    {
        var viewModel = CreateViewModel(administrationService: new ThrowingAdministrationService());

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Refresh failed.", viewModel.EntityBrowserStatus);
        Assert.Equal("Administration endpoint unavailable.", viewModel.EntityBrowserError);
        Assert.Contains("Refresh failed: Administration endpoint unavailable.", viewModel.OperationLog[0].Message);
    }

    [Fact]
    public async Task CreateQueueCommand_runs_workflow_and_refreshes_tree()
    {
        var administrationService = new FakeAdministrationService([]);
        var workflow = new FakeEntityManagementWorkflow
        {
            CreateQueueResult = EntityManagementOperationResult.ChangedWithLog("Created queue orders.")
        };
        var viewModel = CreateViewModel(administrationService: administrationService, workflow: workflow);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        await viewModel.CreateQueueCommand.ExecuteAsync(null);

        Assert.True(workflow.CreateQueueCalled);
        Assert.Equal(2, administrationService.GetEntityTreeCallCount);
        Assert.Contains("Created queue orders.", viewModel.OperationLog[1].Message);
    }

    [Fact]
    public async Task CreateCommands_are_disabled_until_connected()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CreateQueueCommand.CanExecute(null));
        Assert.False(viewModel.CreateTopicCommand.CanExecute(null));
        Assert.False(viewModel.CreateSubscriptionCommand.CanExecute(null));

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.CreateQueueCommand.CanExecute(null));
        Assert.True(viewModel.CreateTopicCommand.CanExecute(null));
        Assert.True(viewModel.CreateSubscriptionCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdateSelectedEntityCommand_runs_workflow_for_selected_queue()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 0);
        var administrationService = new FakeAdministrationService([queue]);
        var workflow = new FakeEntityManagementWorkflow
        {
            UpdateResult = EntityManagementOperationResult.ChangedWithLog("Updated queue orders.")
        };
        var viewModel = CreateViewModel(administrationService: administrationService, workflow: workflow);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);
        await viewModel.UpdateSelectedEntityCommand.ExecuteAsync(null);

        Assert.Equal(queue, workflow.UpdatedEntity);
        Assert.Equal(2, administrationService.GetEntityTreeCallCount);
        Assert.Contains("Updated queue orders.", viewModel.OperationLog[1].Message);
    }

    [Fact]
    public async Task DeleteSelectedEntityCommand_requires_confirmation_before_deleting()
    {
        ServiceBusEntityNode topic = CreateEntity(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0);
        ServiceBusEntityNode subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 0, deadLetter: 0);
        var administrationService = new FakeAdministrationService([topic, subscription]);
        var workflow = new FakeEntityManagementWorkflow
        {
            DeleteResult = EntityManagementOperationResult.ChangedWithLog("Deleted subscription events/subscriptions/billing.")
        };
        var viewModel = CreateViewModel(administrationService: administrationService, workflow: workflow);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[1].Children[0].Children[0]);
        await viewModel.DeleteSelectedEntityCommand.ExecuteAsync(null);

        Assert.Equal(subscription, workflow.DeletedEntity);
        Assert.Equal(2, administrationService.GetEntityTreeCallCount);
        Assert.Contains("Deleted subscription events/subscriptions/billing.", viewModel.OperationLog[1].Message);
    }

    [Fact]
    public async Task Message_commands_are_enabled_for_supported_entity_kinds()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 1, deadLetter: 0);
        ServiceBusEntityNode topic = CreateEntity(EntityKind.Topic, "events", topicName: null, active: 0, deadLetter: 0);
        ServiceBusEntityNode subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 1, deadLetter: 0);
        var viewModel = CreateViewModel(administrationService: new FakeAdministrationService([queue, topic, subscription]));

        Assert.False(viewModel.MessageInspection.SendMessageCommand.CanExecute(null));
        Assert.False(viewModel.MessageInspection.PeekActiveMessagesCommand.CanExecute(null));

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);

        Assert.True(viewModel.MessageInspection.SendMessageCommand.CanExecute(null));
        Assert.True(viewModel.MessageInspection.PeekActiveMessagesCommand.CanExecute(null));

        viewModel.SelectEntity(viewModel.EntityTree[1].Children[0]);

        Assert.True(viewModel.MessageInspection.SendMessageCommand.CanExecute(null));
        Assert.False(viewModel.MessageInspection.PeekActiveMessagesCommand.CanExecute(null));

        viewModel.SelectEntity(viewModel.EntityTree[1].Children[0].Children[0]);

        Assert.False(viewModel.MessageInspection.SendMessageCommand.CanExecute(null));
        Assert.True(viewModel.MessageInspection.PeekActiveMessagesCommand.CanExecute(null));
    }

    [Fact]
    public async Task PeekActiveMessagesCommand_loads_grid_and_detail_properties()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 1, deadLetter: 0);
        ExplorerMessage message = CreateMessage(sequenceNumber: 7, "hello service bus", new Dictionary<string, object?>
        {
            ["source"] = "test"
        });
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        var viewModel = CreateViewModel(
            administrationService: new FakeAdministrationService([queue]),
            messageService: messageService);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);
        await viewModel.MessageInspection.PeekActiveMessagesCommand.ExecuteAsync(null);

        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), messageService.PeekAddress);
        Assert.Equal(MessageBucket.Active, messageService.PeekBucket);
        Assert.Null(messageService.PeekFromSequenceNumber);
        Assert.Single(viewModel.MessageInspection.ActiveMessages);
        Assert.Equal("hello service bus", viewModel.MessageInspection.SelectedBody);
        Assert.Contains("SequenceNumber: 7", viewModel.MessageInspection.SelectedSystemProperties);
        Assert.Contains("source: test", viewModel.MessageInspection.SelectedApplicationProperties);
        Assert.True(viewModel.MessageInspection.PeekNextActiveMessagesCommand.CanExecute(null));

        await viewModel.MessageInspection.PeekNextActiveMessagesCommand.ExecuteAsync(null);

        Assert.Equal(8, messageService.PeekFromSequenceNumber);
    }

    [Fact]
    public async Task PeekDeadLetterMessagesCommand_pages_by_next_sequence_number()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 2);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        var viewModel = CreateViewModel(
            administrationService: new FakeAdministrationService([queue]),
            messageService: messageService);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);
        await viewModel.MessageInspection.PeekDeadLetterMessagesCommand.ExecuteAsync(null);

        Assert.Equal(MessageBucket.DeadLetter, messageService.PeekBucket);
        Assert.Null(messageService.PeekFromSequenceNumber);
        Assert.True(viewModel.MessageInspection.PeekNextDeadLetterMessagesCommand.CanExecute(null));

        await viewModel.MessageInspection.PeekNextDeadLetterMessagesCommand.ExecuteAsync(null);

        Assert.Equal(18, messageService.PeekFromSequenceNumber);
    }

    [Fact]
    public async Task Shell_commands_are_disabled_while_message_operation_is_running()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 1, deadLetter: 0);
        var messageService = new FakeMessageService();
        messageService.BlockPeekUntilReleased();
        var viewModel = CreateViewModel(
            administrationService: new FakeAdministrationService([queue]),
            messageService: messageService);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);
        Task peekTask = viewModel.MessageInspection.PeekActiveMessagesCommand.ExecuteAsync(null);
        await messageService.WaitUntilPeekStartedAsync();

        Assert.False(viewModel.DisconnectCommand.CanExecute(null));
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.UpdateSelectedEntityCommand.CanExecute(null));
        Assert.False(viewModel.DeleteSelectedEntityCommand.CanExecute(null));

        messageService.ReleasePeek();
        await peekTask;
    }

    [Fact]
    public async Task PeekActiveMessagesCommand_ignores_results_when_selection_changes_before_completion()
    {
        ServiceBusEntityNode firstQueue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 1, deadLetter: 0);
        ServiceBusEntityNode secondQueue = CreateEntity(EntityKind.Queue, "billing", topicName: null, active: 0, deadLetter: 0);
        var messageService = new FakeMessageService
        {
            PeekResult = [CreateMessage(sequenceNumber: 7, "stale body", new Dictionary<string, object?>())]
        };
        messageService.BlockPeekUntilReleased();
        var viewModel = CreateViewModel(
            administrationService: new FakeAdministrationService([firstQueue, secondQueue]),
            messageService: messageService);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);
        Task peekTask = viewModel.MessageInspection.PeekActiveMessagesCommand.ExecuteAsync(null);
        await messageService.WaitUntilPeekStartedAsync();

        viewModel.SelectEntity(viewModel.EntityTree[0].Children[1]);
        messageService.ReleasePeek();
        await peekTask;

        Assert.Empty(viewModel.MessageInspection.ActiveMessages);
        Assert.Equal("No message selected.", viewModel.MessageInspection.SelectedBody);
    }

    [Fact]
    public async Task SendMessageCommand_uses_dialog_result_and_refreshes_active_messages_for_queue()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 0);
        var sendCommand = new SendMessageCommand(
            new EntityAddress(EntityKind.Queue, "orders"),
            "payload",
            ContentType: "application/json",
            ApplicationProperties: new Dictionary<string, object?> { ["kind"] = "unit" });
        var messageDialog = new FakeMessageDialogService { SendResult = sendCommand };
        var messageService = new FakeMessageService();
        var viewModel = CreateViewModel(
            administrationService: new FakeAdministrationService([queue]),
            messageService: messageService,
            messageDialogService: messageDialog);

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectEntity(viewModel.EntityTree[0].Children[0]);
        await viewModel.MessageInspection.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(queue, messageDialog.Entity);
        Assert.Equal(sendCommand, messageService.SentCommand);
        Assert.Equal(1, messageService.PeekCallCount);
        Assert.Contains("Sent message to orders.", viewModel.OperationLog[1].Message);
    }

    private static ShellViewModel CreateViewModel(
        IConnectionProfileStore? store = null,
        IServiceBusClientFactory? clientFactory = null,
        IServiceBusAdministrationService? administrationService = null,
        IServiceBusMessageService? messageService = null,
        IEntityManagementWorkflow? workflow = null,
        IMessageDialogService? messageDialogService = null)
    {
        return new ShellViewModel(
            store ?? new FakeProfileStore([ConnectionProfileDefaults.LocalEmulator]),
            clientFactory ?? new FakeClientFactory(),
            administrationService ?? new FakeAdministrationService([]),
            messageService ?? new FakeMessageService(),
            workflow ?? new FakeEntityManagementWorkflow(),
            messageDialogService ?? new FakeMessageDialogService(),
            new FixedClock());
    }

    private static ServiceBusEntityNode CreateEntity(
        EntityKind kind,
        string name,
        string? topicName,
        long active,
        long deadLetter)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            active,
            deadLetter,
            ScheduledMessageCount: 0,
            Status: "Active",
            new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 8, 11, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            RequiresSession: false,
            RequiresDuplicateDetection: false));
    }

    private static ExplorerMessage CreateMessage(
        long sequenceNumber,
        string body,
        IReadOnlyDictionary<string, object?> applicationProperties)
    {
        return new ExplorerMessage(
            "message-1",
            sequenceNumber,
            body,
            body,
            body.Length,
            new DateTimeOffset(2026, 7, 8, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 9, 10, 30, 0, TimeSpan.Zero),
            DeliveryCount: 1,
            ContentType: "text/plain",
            CorrelationId: "correlation-1",
            SessionId: null,
            Subject: "subject-1",
            applicationProperties,
            new Dictionary<string, object?>
            {
                ["SequenceNumber"] = sequenceNumber,
                ["EnqueuedTimeUtc"] = new DateTimeOffset(2026, 7, 8, 10, 30, 0, TimeSpan.Zero)
            });
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeProfileStore(IReadOnlyList<ConnectionProfile> profiles) : IConnectionProfileStore
    {
        public IReadOnlyList<ConnectionProfile> SavedProfiles { get; private set; } = [];

        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(profiles);
        }

        public Task SaveAsync(IReadOnlyList<ConnectionProfile> savedProfiles, CancellationToken cancellationToken)
        {
            SavedProfiles = savedProfiles;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProfileStore : IConnectionProfileStore
    {
        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Bad profile JSON.");
        }

        public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClientFactory : IServiceBusClientFactory
    {
        public bool ConnectCalled { get; private set; }

        public ServiceBusAdministrationClient AdministrationClient =>
            throw new NotSupportedException("The shell tests do not use a live administration client.");

        public ServiceBusClient RuntimeClient =>
            throw new NotSupportedException("The shell tests do not use a live runtime client.");

        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAdministrationService(IReadOnlyList<ServiceBusEntityNode> entities) : IServiceBusAdministrationService
    {
        public bool GetEntityTreeCalled { get; private set; }

        public int GetEntityTreeCallCount { get; private set; }

        public CreateQueueCommand? CreatedQueue { get; private set; }

        public UpdateQueueCommand? UpdatedQueue { get; private set; }

        public (string TopicName, string SubscriptionName)? DeletedSubscription { get; private set; }

        public Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
        {
            GetEntityTreeCalled = true;
            GetEntityTreeCallCount++;
            return Task.FromResult(entities);
        }

        public Task CreateQueueAsync(CreateQueueCommand command, CancellationToken cancellationToken)
        {
            CreatedQueue = command;
            return Task.CompletedTask;
        }

        public Task UpdateQueueAsync(UpdateQueueCommand command, CancellationToken cancellationToken)
        {
            UpdatedQueue = command;
            return Task.CompletedTask;
        }

        public Task DeleteQueueAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task CreateTopicAsync(CreateTopicCommand command, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UpdateTopicAsync(UpdateTopicCommand command, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteTopicAsync(string name, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UpdateSubscriptionAsync(UpdateSubscriptionCommand command, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            DeletedSubscription = (topicName, subscriptionName);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAdministrationService : IServiceBusAdministrationService
    {
        public Task<IReadOnlyList<ServiceBusEntityNode>> GetEntityTreeAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Administration endpoint unavailable.");
        }

        public Task CreateQueueAsync(CreateQueueCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateQueueAsync(UpdateQueueCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteQueueAsync(string name, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CreateTopicAsync(CreateTopicCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateTopicAsync(UpdateTopicCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteTopicAsync(string name, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CreateSubscriptionAsync(CreateSubscriptionCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateSubscriptionAsync(UpdateSubscriptionCommand command, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeMessageService : IServiceBusMessageService
    {
        private TaskCompletionSource? _peekStarted;
        private TaskCompletionSource? _releasePeek;

        public IReadOnlyList<ExplorerMessage> PeekResult { get; init; } = [];

        public EntityAddress? PeekAddress { get; private set; }

        public MessageBucket? PeekBucket { get; private set; }

        public long? PeekFromSequenceNumber { get; private set; }

        public int PeekCallCount { get; private set; }

        public SendMessageCommand? SentCommand { get; private set; }

        public void BlockPeekUntilReleased()
        {
            _peekStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releasePeek = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task WaitUntilPeekStartedAsync()
        {
            if (_peekStarted is not null)
            {
                await _peekStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public void ReleasePeek()
        {
            _releasePeek?.TrySetResult();
        }

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            PeekAddress = address;
            PeekBucket = bucket;
            PeekFromSequenceNumber = fromSequenceNumber;
            PeekCallCount++;
            _peekStarted?.TrySetResult();
            return CompletePeekAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<ExplorerMessage>> CompletePeekAsync(CancellationToken cancellationToken)
        {
            if (_releasePeek is not null)
            {
                await _releasePeek.Task.WaitAsync(cancellationToken);
            }

            return PeekResult;
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
        {
            SentCommand = command;
            return Task.CompletedTask;
        }

    }

    private sealed class FakeMessageDialogService : IMessageDialogService
    {
        public SendMessageCommand? SendResult { get; init; }

        public ServiceBusEntityNode? Entity { get; private set; }

        public Task<SendMessageCommand?> ShowSendMessageDialogAsync(ServiceBusEntityNode entity)
        {
            Entity = entity;
            return Task.FromResult(SendResult);
        }
    }

    private sealed class FakeEntityManagementWorkflow : IEntityManagementWorkflow
    {
        public bool CreateQueueCalled { get; private set; }

        public EntityManagementOperationResult CreateQueueResult { get; init; } = EntityManagementOperationResult.NoChange;

        public EntityManagementOperationResult UpdateResult { get; init; } = EntityManagementOperationResult.NoChange;

        public EntityManagementOperationResult DeleteResult { get; init; } = EntityManagementOperationResult.NoChange;

        public ServiceBusEntityNode? UpdatedEntity { get; private set; }

        public ServiceBusEntityNode? DeletedEntity { get; private set; }

        public Task<EntityManagementOperationResult> CreateQueueAsync(CancellationToken cancellationToken)
        {
            CreateQueueCalled = true;
            return Task.FromResult(CreateQueueResult);
        }

        public Task<EntityManagementOperationResult> CreateTopicAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(EntityManagementOperationResult.NoChange);
        }

        public Task<EntityManagementOperationResult> CreateSubscriptionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(EntityManagementOperationResult.NoChange);
        }

        public Task<EntityManagementOperationResult> UpdateAsync(
            ServiceBusEntityNode entity,
            CancellationToken cancellationToken)
        {
            UpdatedEntity = entity;
            return Task.FromResult(UpdateResult);
        }

        public Task<EntityManagementOperationResult> DeleteAsync(
            ServiceBusEntityNode entity,
            CancellationToken cancellationToken)
        {
            DeletedEntity = entity;
            return Task.FromResult(DeleteResult);
        }
    }
}
