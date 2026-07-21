using Azure.Messaging.ServiceBus;
using Azure;
using ServiceBusEmulatorExplorer.App;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.App.ViewModels;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests.ViewModels;

public sealed class MessageInspectionViewModelTests
{
    [Fact]
    public void Disconnected_message_tooltips_are_connection_mode_neutral()
    {
        MessageInspectionViewModel viewModel = CreateViewModel();

        Assert.Contains("Connect first.", viewModel.PeekActiveMessagesCommandToolTip, StringComparison.Ordinal);
        Assert.DoesNotContain("emulator", viewModel.PeekActiveMessagesCommandToolTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorization_failure_keeps_message_inspection_connected_and_preserves_non_secret_detail_in_log()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 0);
        var messageService = new FakeMessageService
        {
            PeekException = new RequestFailedException(403, "Entity authorization denied", "AuthorizationFailed", null)
        };
        List<string> log = [];
        MessageInspectionViewModel viewModel = CreateViewModel(messageService, log: log);
        viewModel.AuthenticationMode = ConnectionAuthenticationMode.AzureCli;
        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);

        await viewModel.PeekActiveMessagesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Message operation failed.", viewModel.Status);
        Assert.Contains("permission", viewModel.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(log, entry => entry.Contains("HTTP 403 (AuthorizationFailed).", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_authorization_failure_keeps_message_inspection_connected()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 0);
        var messageService = new FakeMessageService
        {
            SendException = new RequestFailedException(403, "Send authorization denied", "AuthorizationFailed", null)
        };
        var messageDialog = new FakeMessageDialogService
        {
            SendResult = new SendMessageCommand(new EntityAddress(EntityKind.Queue, "orders"), "non-sensitive payload")
        };
        MessageInspectionViewModel viewModel = CreateViewModel(messageService, messageDialogService: messageDialog);
        viewModel.AuthenticationMode = ConnectionAuthenticationMode.AzureCli;
        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);

        await viewModel.SendMessageCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Contains("permission", viewModel.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessageCommand_preserves_success_when_refresh_fails()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 0);
        var sendCommand = new SendMessageCommand(
            new EntityAddress(EntityKind.Queue, "orders"),
            "payload");
        var messageService = new FakeMessageService
        {
            PeekException = new InvalidOperationException("refresh broke")
        };
        var messageDialog = new FakeMessageDialogService { SendResult = sendCommand };
        List<string> log = [];
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            messageDialogService: messageDialog,
            log: log);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(1, messageService.SendCallCount);
        Assert.Equal("Sent message to orders.", viewModel.Status);
        Assert.Equal("Operation succeeded, but refresh failed: The operation failed. Detail: InvalidOperationException; raw exception details are omitted.", viewModel.Error);
        Assert.Contains(log, entry => entry.Contains("Sent message to orders.", StringComparison.Ordinal));
        Assert.Contains(log, entry => entry.Contains("Operation succeeded, but refresh failed: The operation failed.", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Contains("Send message failed", StringComparison.Ordinal));

        await viewModel.PeekActiveMessagesCommand.ExecuteAsync(null);

        Assert.Equal(1, messageService.SendCallCount);
    }

    [Fact]
    public async Task SendMessageCommand_preserves_success_when_log_sink_fails_after_commit()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 0);
        var sendCommand = new SendMessageCommand(
            new EntityAddress(EntityKind.Queue, "orders"),
            "payload");
        var messageService = new FakeMessageService();
        var messageDialog = new FakeMessageDialogService { SendResult = sendCommand };
        List<string> log = [];
        Action<string> addLog = entry =>
        {
            log.Add(entry);
            if (entry == "Sent message to orders.")
            {
                throw new InvalidOperationException("log broke");
            }
        };
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            messageDialogService: messageDialog,
            addLog: addLog);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(1, messageService.SendCallCount);
        Assert.Equal(1, messageService.PeekCallCount);
        Assert.Equal("Sent message to orders.", viewModel.Status);
        Assert.Equal("Operation succeeded, but reporting failed: log broke", viewModel.Error);
        Assert.DoesNotContain(log, entry => entry.Contains("Send message failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeadLetter_commands_enable_after_selecting_dlq_message()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        MessageInspectionViewModel viewModel = CreateViewModel(messageService: messageService);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);

        Assert.False(viewModel.ReplayDeadLetterCommand.CanExecute(null));
        Assert.False(viewModel.DeleteSelectedDeadLetterCommand.CanExecute(null));
        Assert.False(viewModel.DeleteVisibleDeadLetterCommand.CanExecute(null));

        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);

        Assert.False(viewModel.ReplayDeadLetterCommand.CanExecute(null));
        Assert.False(viewModel.EditAndReplayDeadLetterCommand.CanExecute(null));
        Assert.False(viewModel.DeleteSelectedDeadLetterCommand.CanExecute(null));
        Assert.True(viewModel.DeleteVisibleDeadLetterCommand.CanExecute(null));

        viewModel.SelectDeadLetterMessages([message]);

        Assert.True(viewModel.ReplayDeadLetterCommand.CanExecute(null));
        Assert.True(viewModel.EditAndReplayDeadLetterCommand.CanExecute(null));
        Assert.True(viewModel.DeleteSelectedDeadLetterCommand.CanExecute(null));
        Assert.True(viewModel.DeleteVisibleDeadLetterCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReplayDeadLetterCommand_replays_copy_without_deleting_original_and_refreshes_messages()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>
        {
            ["source"] = "dlq"
        });
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        var replayService = new FakeDeadLetterReplayService
        {
            ReplayResult = new ReplayResult("new-message-id", OriginalDeleted: false)
        };
        List<string> log = [];
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            replayService,
            log: log);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([message]);
        await viewModel.ReplayDeadLetterCommand.ExecuteAsync(null);

        Assert.NotNull(replayService.ReplayRequest);
        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), replayService.ReplayRequest.Source);
        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), replayService.ReplayRequest.Destination);
        Assert.Equal(17, replayService.ReplayRequest.SequenceNumber);
        Assert.False(replayService.ReplayRequest.DeleteOriginal);
        Assert.Null(replayService.ReplayRequest.EditedBody);
        Assert.Null(replayService.ReplayRequest.ApplicationProperties);
        Assert.Equal(3, messageService.PeekCallCount);
        Assert.Contains(log, entry => entry.Contains("Original remains in DLQ.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplayDeadLetterCommand_preserves_success_when_refresh_fails()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [message],
            PeekException = new InvalidOperationException("refresh broke"),
            PeekExceptionAfterCallCount = 1
        };
        var replayService = new FakeDeadLetterReplayService
        {
            ReplayResult = new ReplayResult("new-message-id", OriginalDeleted: false)
        };
        List<string> log = [];
        MessageInspectionViewModel viewModel = CreateViewModel(messageService, replayService, log: log);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([message]);
        await viewModel.ReplayDeadLetterCommand.ExecuteAsync(null);

        Assert.Equal(1, replayService.ReplayCallCount);
        Assert.Equal("Replayed DLQ sequence 17 as new-message-id. Original remains in DLQ.", viewModel.Status);
        Assert.Equal("Operation succeeded, but refresh failed: The operation failed. Detail: InvalidOperationException; raw exception details are omitted.", viewModel.Error);
        Assert.Contains(log, entry => entry.Contains("Replayed DLQ sequence 17 to orders as new-message-id. Original remains in DLQ.", StringComparison.Ordinal));
        Assert.Contains(log, entry => entry.Contains("Operation succeeded, but refresh failed: The operation failed.", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Contains("Replay DLQ message failed", StringComparison.Ordinal));

        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);

        Assert.Equal(1, replayService.ReplayCallCount);
    }

    [Fact]
    public async Task EditAndReplayDeadLetterCommand_uses_dialog_result()
    {
        ServiceBusEntityNode subscription = CreateEntity(EntityKind.Subscription, "billing", "events", active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 19, "dead letter body", new Dictionary<string, object?>());
        var edits = new ReplayMessageEdits(
            "edited body",
            ContentType: "application/json",
            CorrelationId: "edited-correlation",
            SessionId: null,
            Subject: "edited-subject",
            new Dictionary<string, object?> { ["edited"] = "true" });
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        var messageDialog = new FakeMessageDialogService { ReplayResult = edits };
        var replayService = new FakeDeadLetterReplayService
        {
            ReplayResult = new ReplayResult("new-message-id", OriginalDeleted: false)
        };
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            replayService,
            messageDialog);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(subscription);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([message]);
        await viewModel.EditAndReplayDeadLetterCommand.ExecuteAsync(null);

        Assert.Equal(subscription, messageDialog.ReplayEntity);
        Assert.Equal(message, messageDialog.ReplayMessage);
        Assert.Equal(new EntityAddress(EntityKind.Subscription, "billing", "events"), replayService.ReplayRequest?.Source);
        Assert.Equal(new EntityAddress(EntityKind.Topic, "events"), replayService.ReplayRequest?.Destination);
        Assert.Equal(19, replayService.ReplayRequest?.SequenceNumber);
        Assert.Equal("edited body", replayService.ReplayRequest?.EditedBody);
        Assert.Equal("true", replayService.ReplayRequest?.ApplicationProperties?["edited"]);
    }

    [Fact]
    public async Task DeleteSelectedDeadLetterCommand_requires_confirmation_before_deleting()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        var messageDialog = new FakeMessageDialogService { ConfirmDeleteResult = true };
        var replayService = new FakeDeadLetterReplayService();
        List<string> log = [];
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            replayService,
            messageDialog,
            log);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([message]);
        await viewModel.DeleteSelectedDeadLetterCommand.ExecuteAsync(null);

        Assert.Equal(queue, messageDialog.DeleteEntity);
        Assert.False(messageDialog.DeleteVisiblePage);
        Assert.Equal(17, Assert.Single(messageDialog.DeleteMessages).SequenceNumber);
        Assert.Equal(new EntityAddress(EntityKind.Queue, "orders"), replayService.DeleteRequest?.Source);
        Assert.Equal(17, Assert.Single(replayService.DeleteRequest!.SequenceNumbers));
        Assert.Equal(2, messageService.PeekCallCount);
        Assert.Contains(log, entry => entry.Contains("Deleted 1 DLQ message(s) from orders.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteSelectedDeadLetterCommand_preserves_success_when_refresh_fails()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [message],
            PeekException = new InvalidOperationException("refresh broke"),
            PeekExceptionAfterCallCount = 1
        };
        var messageDialog = new FakeMessageDialogService { ConfirmDeleteResult = true };
        var replayService = new FakeDeadLetterReplayService();
        List<string> log = [];
        MessageInspectionViewModel viewModel = CreateViewModel(messageService, replayService, messageDialog, log);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([message]);
        await viewModel.DeleteSelectedDeadLetterCommand.ExecuteAsync(null);

        Assert.Equal(1, replayService.DeleteCallCount);
        Assert.Equal("Deleted 1 DLQ message(s).", viewModel.Status);
        Assert.Equal("Operation succeeded, but refresh failed: The operation failed. Detail: InvalidOperationException; raw exception details are omitted.", viewModel.Error);
        Assert.Contains(log, entry => entry.Contains("Deleted 1 DLQ message(s) from orders.", StringComparison.Ordinal));
        Assert.Contains(log, entry => entry.Contains("Operation succeeded, but refresh failed: The operation failed.", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Contains("Delete DLQ messages failed", StringComparison.Ordinal));

        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);

        Assert.Equal(1, replayService.DeleteCallCount);
    }

    [Fact]
    public async Task DeleteSelectedDeadLetterCommand_deletes_all_selected_dlq_messages()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 2);
        ExplorerMessage firstMessage = CreateMessage(sequenceNumber: 17, "first dead letter", new Dictionary<string, object?>());
        ExplorerMessage secondMessage = CreateMessage(sequenceNumber: 18, "second dead letter", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [firstMessage, secondMessage]
        };
        var messageDialog = new FakeMessageDialogService { ConfirmDeleteResult = true };
        var replayService = new FakeDeadLetterReplayService
        {
            DeleteResult = new DeleteDeadLetterMessagesResult(2)
        };
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            replayService,
            messageDialog);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([firstMessage, secondMessage]);
        await viewModel.DeleteSelectedDeadLetterCommand.ExecuteAsync(null);

        Assert.Equal([17, 18], replayService.DeleteRequest?.SequenceNumbers);
        Assert.Equal([17, 18], messageDialog.DeleteMessages.Select(message => message.SequenceNumber));
    }

    [Fact]
    public async Task DeleteVisibleDeadLetterCommand_deletes_visible_page_with_visible_confirmation_flag()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 2);
        ExplorerMessage firstMessage = CreateMessage(sequenceNumber: 17, "first dead letter", new Dictionary<string, object?>());
        ExplorerMessage secondMessage = CreateMessage(sequenceNumber: 18, "second dead letter", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [firstMessage, secondMessage]
        };
        var messageDialog = new FakeMessageDialogService { ConfirmDeleteResult = true };
        var replayService = new FakeDeadLetterReplayService
        {
            DeleteResult = new DeleteDeadLetterMessagesResult(2)
        };
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            replayService,
            messageDialog);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        await viewModel.DeleteVisibleDeadLetterCommand.ExecuteAsync(null);

        Assert.True(messageDialog.DeleteVisiblePage);
        Assert.Equal([17, 18], replayService.DeleteRequest?.SequenceNumbers);
        Assert.Equal([17, 18], messageDialog.DeleteMessages.Select(message => message.SequenceNumber));
    }

    [Fact]
    public async Task DeleteSelectedDeadLetterCommand_cancels_without_deleting()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 0, deadLetter: 1);
        ExplorerMessage message = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        var messageService = new FakeMessageService
        {
            PeekResult = [message]
        };
        var messageDialog = new FakeMessageDialogService { ConfirmDeleteResult = false };
        var replayService = new FakeDeadLetterReplayService();
        MessageInspectionViewModel viewModel = CreateViewModel(
            messageService,
            replayService,
            messageDialog);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        await viewModel.PeekDeadLetterMessagesCommand.ExecuteAsync(null);
        viewModel.SelectDeadLetterMessages([message]);
        await viewModel.DeleteSelectedDeadLetterCommand.ExecuteAsync(null);

        Assert.Null(replayService.DeleteRequest);
    }

    [Fact]
    public async Task Selecting_active_message_clears_stale_dlq_command_selection()
    {
        ServiceBusEntityNode queue = CreateEntity(EntityKind.Queue, "orders", topicName: null, active: 1, deadLetter: 1);
        ExplorerMessage deadLetterMessage = CreateMessage(sequenceNumber: 17, "dead letter body", new Dictionary<string, object?>());
        ExplorerMessage activeMessage = CreateMessage(sequenceNumber: 7, "active body", new Dictionary<string, object?>());
        var replayService = new FakeDeadLetterReplayService();
        MessageInspectionViewModel viewModel = CreateViewModel(deadLetterReplayService: replayService);

        viewModel.IsConnected = true;
        viewModel.SelectEntity(queue);
        viewModel.SelectDeadLetterMessages([deadLetterMessage]);

        Assert.True(viewModel.DeleteSelectedDeadLetterCommand.CanExecute(null));

        viewModel.SelectActiveMessage(activeMessage);
        await viewModel.DeleteSelectedDeadLetterCommand.ExecuteAsync(null);

        Assert.False(viewModel.ReplayDeadLetterCommand.CanExecute(null));
        Assert.False(viewModel.EditAndReplayDeadLetterCommand.CanExecute(null));
        Assert.False(viewModel.DeleteSelectedDeadLetterCommand.CanExecute(null));
        Assert.Null(replayService.DeleteRequest);
    }

    [Fact]
    public void Replay_dialog_preserves_original_application_property_types_when_property_text_is_unchanged()
    {
        var originalProperties = new Dictionary<string, object?>
        {
            ["count"] = 3,
            ["enabled"] = true
        };

        IReadOnlyDictionary<string, object?> editedProperties =
            ReplayDeadLetterDialog.CreateEditedApplicationProperties(
                originalProperties,
                "count=3\r\nenabled=True",
                "count=3\r\nenabled=True");

        Assert.Same(originalProperties, editedProperties);
        Assert.Equal(3, editedProperties["count"]);
        Assert.Equal(true, editedProperties["enabled"]);
    }

    private static MessageInspectionViewModel CreateViewModel(
        IServiceBusMessageService? messageService = null,
        IDeadLetterReplayService? deadLetterReplayService = null,
        IMessageDialogService? messageDialogService = null,
        List<string>? log = null,
        Action<string>? addLog = null)
    {
        List<string> operationLog = log ?? [];
        return new MessageInspectionViewModel(
            messageService ?? new FakeMessageService(),
            deadLetterReplayService ?? new FakeDeadLetterReplayService(),
            messageDialogService ?? new FakeMessageDialogService(),
            addLog ?? operationLog.Add);
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

    private sealed class FakeMessageService : IServiceBusMessageService
    {
        public IReadOnlyList<ExplorerMessage> PeekResult { get; init; } = [];

        public Exception? PeekException { get; init; }

        public Exception? SendException { get; init; }

        public int PeekExceptionAfterCallCount { get; init; }

        public int PeekCallCount { get; private set; }

        public int SendCallCount { get; private set; }

        public Task<IReadOnlyList<ExplorerMessage>> PeekMessagesAsync(
            EntityAddress address,
            MessageBucket bucket,
            int take,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            PeekCallCount++;
            if (PeekException is not null && PeekCallCount > PeekExceptionAfterCallCount)
            {
                throw PeekException;
            }

            return Task.FromResult(PeekResult);
        }

        public Task SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken)
        {
            SendCallCount++;
            if (SendException is not null)
            {
                throw SendException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeadLetterReplayService : IDeadLetterReplayService
    {
        public ReplayRequest? ReplayRequest { get; private set; }

        public int ReplayCallCount { get; private set; }

        public ReplayResult ReplayResult { get; init; } = new("new-message-id", OriginalDeleted: false);

        public DeleteDeadLetterMessagesRequest? DeleteRequest { get; private set; }

        public int DeleteCallCount { get; private set; }

        public DeleteDeadLetterMessagesResult DeleteResult { get; init; } = new(1);

        public Task<ReplayResult> ReplayAsync(ReplayRequest request, CancellationToken cancellationToken)
        {
            ReplayRequest = request;
            ReplayCallCount++;
            return Task.FromResult(ReplayResult);
        }

        public Task<DeleteDeadLetterMessagesResult> DeleteAsync(
            DeleteDeadLetterMessagesRequest request,
            CancellationToken cancellationToken)
        {
            DeleteRequest = request;
            DeleteCallCount++;
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class FakeMessageDialogService : IMessageDialogService
    {
        public SendMessageCommand? SendResult { get; init; }

        public ReplayMessageEdits? ReplayResult { get; init; }

        public ServiceBusEntityNode? ReplayEntity { get; private set; }

        public ExplorerMessage? ReplayMessage { get; private set; }

        public bool ConfirmDeleteResult { get; init; }

        public ServiceBusEntityNode? DeleteEntity { get; private set; }

        public IReadOnlyList<ExplorerMessage> DeleteMessages { get; private set; } = [];

        public bool DeleteVisiblePage { get; private set; }

        public Task<SendMessageCommand?> ShowSendMessageDialogAsync(ServiceBusEntityNode entity)
        {
            return Task.FromResult(SendResult);
        }

        public Task<ReplayMessageEdits?> ShowReplayDeadLetterDialogAsync(
            ServiceBusEntityNode entity,
            ExplorerMessage message)
        {
            ReplayEntity = entity;
            ReplayMessage = message;
            return Task.FromResult(ReplayResult);
        }

        public Task<bool> ConfirmDeleteDeadLetterMessagesAsync(
            ServiceBusEntityNode entity,
            IReadOnlyList<ExplorerMessage> messages,
            bool visiblePage)
        {
            DeleteEntity = entity;
            DeleteMessages = messages;
            DeleteVisiblePage = visiblePage;
            return Task.FromResult(ConfirmDeleteResult);
        }
    }
}
