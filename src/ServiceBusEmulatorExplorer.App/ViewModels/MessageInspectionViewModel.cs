using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.ViewModels;

public sealed class MessageInspectionViewModel : ObservableObject
{
    private readonly IServiceBusMessageService _messageService;
    private readonly IDeadLetterReplayService _deadLetterReplayService;
    private readonly IMessageDialogService _messageDialogService;
    private readonly Action<string> _addLog;
    private ServiceBusEntityNode? _selectedEntity;
    private ExplorerMessage? _selectedDeadLetterMessage;
    private IReadOnlyList<ExplorerMessage> _selectedDeadLetterMessages = [];
    private string _status = "Select a queue or subscription to inspect messages.";
    private string? _error;
    private string _selectedBody = "No message selected.";
    private string _selectedSystemProperties = "";
    private string _selectedApplicationProperties = "";
    private bool _isConnected;
    private bool _isShellBusy;
    private bool _isBusy;
    private long? _nextActiveSequenceNumber;
    private long? _nextDeadLetterSequenceNumber;
    private int _selectionVersion;

    public MessageInspectionViewModel(
        IServiceBusMessageService messageService,
        IDeadLetterReplayService deadLetterReplayService,
        IMessageDialogService messageDialogService,
        Action<string> addLog)
    {
        _messageService = messageService;
        _deadLetterReplayService = deadLetterReplayService;
        _messageDialogService = messageDialogService;
        _addLog = addLog;
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
        PeekActiveMessagesCommand = new AsyncRelayCommand(PeekActiveMessagesAsync, CanPeekMessages);
        PeekNextActiveMessagesCommand = new AsyncRelayCommand(PeekNextActiveMessagesAsync, CanPeekNextActiveMessages);
        PeekDeadLetterMessagesCommand = new AsyncRelayCommand(PeekDeadLetterMessagesAsync, CanPeekMessages);
        PeekNextDeadLetterMessagesCommand = new AsyncRelayCommand(PeekNextDeadLetterMessagesAsync, CanPeekNextDeadLetterMessages);
        ReplayDeadLetterCommand = new AsyncRelayCommand(ReplayDeadLetterAsync, CanUseSelectedDeadLetterMessage);
        EditAndReplayDeadLetterCommand = new AsyncRelayCommand(EditAndReplayDeadLetterAsync, CanUseSelectedDeadLetterMessage);
        DeleteSelectedDeadLetterCommand = new AsyncRelayCommand(DeleteSelectedDeadLetterAsync, CanDeleteSelectedDeadLetterMessages);
        DeleteVisibleDeadLetterCommand = new AsyncRelayCommand(DeleteVisibleDeadLetterAsync, CanDeleteVisibleDeadLetterMessages);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string SelectedBody
    {
        get => _selectedBody;
        private set => SetProperty(ref _selectedBody, value);
    }

    public string SelectedSystemProperties
    {
        get => _selectedSystemProperties;
        private set => SetProperty(ref _selectedSystemProperties, value);
    }

    public string SelectedApplicationProperties
    {
        get => _selectedApplicationProperties;
        private set => SetProperty(ref _selectedApplicationProperties, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public bool IsShellBusy
    {
        get => _isShellBusy;
        set
        {
            if (SetProperty(ref _isShellBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public ObservableCollection<ExplorerMessage> ActiveMessages { get; } = [];

    public ObservableCollection<ExplorerMessage> DeadLetterMessages { get; } = [];

    public IAsyncRelayCommand SendMessageCommand { get; }

    public IAsyncRelayCommand PeekActiveMessagesCommand { get; }

    public IAsyncRelayCommand PeekNextActiveMessagesCommand { get; }

    public IAsyncRelayCommand PeekDeadLetterMessagesCommand { get; }

    public IAsyncRelayCommand PeekNextDeadLetterMessagesCommand { get; }

    public IAsyncRelayCommand ReplayDeadLetterCommand { get; }

    public IAsyncRelayCommand EditAndReplayDeadLetterCommand { get; }

    public IAsyncRelayCommand DeleteSelectedDeadLetterCommand { get; }

    public IAsyncRelayCommand DeleteVisibleDeadLetterCommand { get; }

    public void SelectEntity(ServiceBusEntityNode? entity)
    {
        _selectedEntity = entity;
        _selectionVersion++;
        Reset();

        if (entity is not null)
        {
            Status = entity.Kind == EntityKind.Topic
                ? "Topics can send messages. Inspect active and DLQ messages from subscriptions."
                : "Ready to peek active or DLQ messages.";
        }

        NotifyCommandStateChanged();
    }

    public void SelectActiveMessage(ExplorerMessage? message)
    {
        _selectedDeadLetterMessage = null;
        _selectedDeadLetterMessages = [];
        SelectMessage(message);
        NotifyCommandStateChanged();
    }

    public void SelectDeadLetterMessage(ExplorerMessage? message)
    {
        _selectedDeadLetterMessage = message;
        _selectedDeadLetterMessages = message is null ? [] : [message];
        SelectMessage(message);
        NotifyCommandStateChanged();
    }

    public void SelectDeadLetterMessages(IReadOnlyList<ExplorerMessage> messages)
    {
        _selectedDeadLetterMessages = messages;
        _selectedDeadLetterMessage = messages.Count == 1 ? messages[0] : null;
        SelectMessage(messages.FirstOrDefault());
        NotifyCommandStateChanged();
    }

    private void SelectMessage(ExplorerMessage? message)
    {
        if (message is null)
        {
            SelectedBody = "No message selected.";
            SelectedSystemProperties = "";
            SelectedApplicationProperties = "";
            return;
        }

        SelectedBody = message.Body;
        SelectedSystemProperties = FormatProperties(message.SystemProperties);
        SelectedApplicationProperties = FormatProperties(message.ApplicationProperties);
    }

    private static string FormatProperties(IReadOnlyDictionary<string, object?> properties)
    {
        if (properties.Count == 0)
        {
            return "(none)";
        }

        return string.Join(Environment.NewLine, properties.Select(property =>
            $"{property.Key}: {FormatPropertyValue(property.Value)}"));
    }

    private static string FormatPropertyValue(object? value)
    {
        return value switch
        {
            null => "",
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'"),
            DateTime dateTime => new DateTimeOffset(dateTime).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'"),
            _ => value.ToString() ?? ""
        };
    }

    private void Reset()
    {
        ActiveMessages.Clear();
        DeadLetterMessages.Clear();
        _selectedDeadLetterMessage = null;
        _selectedDeadLetterMessages = [];
        _nextActiveSequenceNumber = null;
        _nextDeadLetterSequenceNumber = null;
        SelectMessage(null);
        Error = null;
        Status = "Select a queue or subscription to inspect messages.";
    }

    private async Task SendMessageAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        int selectionVersion = _selectionVersion;
        if (entity is null)
        {
            return;
        }

        SendMessageCommand? command = await _messageDialogService.ShowSendMessageDialogAsync(entity);
        if (command is null)
        {
            return;
        }

        await RunOperationAsync("Send message failed", async cancellationToken =>
        {
            await _messageService.SendMessageAsync(command, cancellationToken);
            _addLog($"Sent message to {command.Destination.Name}.");
            if (CanInspectSelectedEntity() && selectionVersion == _selectionVersion)
            {
                await PeekMessagesAsync(MessageBucket.Active, resetPage: true, selectionVersion, cancellationToken);
            }
        });
    }

    private async Task RunOperationAsync(
        string failurePrefix,
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy || IsShellBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Error = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await operation(timeout.Token);
        }
        catch (Exception ex)
        {
            Status = "Message operation failed.";
            Error = ex.Message;
            _addLog($"{failurePrefix}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PeekActiveMessagesAsync()
    {
        await RunOperationAsync(
            "Peek active messages failed",
            cancellationToken => PeekMessagesAsync(MessageBucket.Active, resetPage: true, _selectionVersion, cancellationToken));
    }

    private async Task PeekNextActiveMessagesAsync()
    {
        await RunOperationAsync(
            "Peek next active messages failed",
            cancellationToken => PeekMessagesAsync(MessageBucket.Active, resetPage: false, _selectionVersion, cancellationToken));
    }

    private async Task PeekDeadLetterMessagesAsync()
    {
        await RunOperationAsync(
            "Peek DLQ messages failed",
            cancellationToken => PeekMessagesAsync(MessageBucket.DeadLetter, resetPage: true, _selectionVersion, cancellationToken));
    }

    private async Task PeekNextDeadLetterMessagesAsync()
    {
        await RunOperationAsync(
            "Peek next DLQ messages failed",
            cancellationToken => PeekMessagesAsync(MessageBucket.DeadLetter, resetPage: false, _selectionVersion, cancellationToken));
    }

    private async Task ReplayDeadLetterAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        ExplorerMessage? message = _selectedDeadLetterMessage;
        int selectionVersion = _selectionVersion;
        if (entity is null || message is null)
        {
            return;
        }

        ReplayRequest request = DeadLetterReplayRequestFactory.CreateCopyRequest(entity, message);
        await ReplayDeadLetterAsync(request, selectionVersion);
    }

    private async Task EditAndReplayDeadLetterAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        ExplorerMessage? message = _selectedDeadLetterMessage;
        int selectionVersion = _selectionVersion;
        if (entity is null || message is null)
        {
            return;
        }

        ReplayMessageEdits? edits = await _messageDialogService.ShowReplayDeadLetterDialogAsync(entity, message);
        if (edits is null)
        {
            return;
        }

        ReplayRequest request = DeadLetterReplayRequestFactory.CreateEditedRequest(entity, message, edits);
        await ReplayDeadLetterAsync(request, selectionVersion);
    }

    private async Task ReplayDeadLetterAsync(ReplayRequest request, int selectionVersion)
    {
        await RunOperationAsync("Replay DLQ message failed", async cancellationToken =>
        {
            ReplayResult result = await _deadLetterReplayService.ReplayAsync(request, cancellationToken);
            string originalStatus = result.OriginalDeleted ? "Original was deleted." : "Original remains in DLQ.";
            Status = "Replayed DLQ message copy.";
            _addLog($"Replayed DLQ sequence {request.SequenceNumber} to {request.Destination.Name} as {result.NewMessageId}. {originalStatus}");

            if (selectionVersion == _selectionVersion)
            {
                await PeekMessagesAsync(MessageBucket.Active, resetPage: true, selectionVersion, cancellationToken);
                await PeekMessagesAsync(MessageBucket.DeadLetter, resetPage: true, selectionVersion, cancellationToken);
            }
        });
    }

    private async Task DeleteSelectedDeadLetterAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        IReadOnlyList<ExplorerMessage> messages = _selectedDeadLetterMessages;
        int selectionVersion = _selectionVersion;
        if (entity is null || messages.Count == 0)
        {
            return;
        }

        await DeleteDeadLetterMessagesAsync(entity, messages, visiblePage: false, selectionVersion);
    }

    private async Task DeleteVisibleDeadLetterAsync()
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        int selectionVersion = _selectionVersion;
        if (entity is null || DeadLetterMessages.Count == 0)
        {
            return;
        }

        await DeleteDeadLetterMessagesAsync(entity, DeadLetterMessages.ToList(), visiblePage: true, selectionVersion);
    }

    private async Task DeleteDeadLetterMessagesAsync(
        ServiceBusEntityNode entity,
        IReadOnlyList<ExplorerMessage> messages,
        bool visiblePage,
        int selectionVersion)
    {
        bool confirmed = await _messageDialogService.ConfirmDeleteDeadLetterMessagesAsync(entity, messages, visiblePage);
        if (!confirmed)
        {
            return;
        }

        DeleteDeadLetterMessagesRequest request = new(
            CreateEntityAddress(entity),
            messages.Select(message => message.SequenceNumber).ToList());

        await RunOperationAsync("Delete DLQ messages failed", async cancellationToken =>
        {
            DeleteDeadLetterMessagesResult result = await _deadLetterReplayService.DeleteAsync(request, cancellationToken);
            Status = $"Deleted {result.DeletedCount} DLQ message(s).";
            _addLog($"Deleted {result.DeletedCount} DLQ message(s) from {entity.Metadata.Path}.");

            if (selectionVersion == _selectionVersion)
            {
                await PeekMessagesAsync(MessageBucket.DeadLetter, resetPage: true, selectionVersion, cancellationToken);
            }
        });
    }

    private async Task PeekMessagesAsync(
        MessageBucket bucket,
        bool resetPage,
        int selectionVersion,
        CancellationToken cancellationToken)
    {
        ServiceBusEntityNode? entity = _selectedEntity;
        if (entity is null)
        {
            return;
        }

        EntityAddress address = CreateEntityAddress(entity);
        long? fromSequenceNumber = resetPage ? null : GetNextSequenceNumber(bucket);
        IReadOnlyList<ExplorerMessage> messages = await _messageService.PeekMessagesAsync(
            address,
            bucket,
            take: 50,
            fromSequenceNumber,
            cancellationToken);
        if (selectionVersion != _selectionVersion)
        {
            return;
        }

        ObservableCollection<ExplorerMessage> target = GetMessageCollection(bucket);
        target.Clear();
        foreach (ExplorerMessage message in messages)
        {
            target.Add(message);
        }

        SetNextSequenceNumber(bucket, messages.Count == 0 ? null : messages.Max(message => message.SequenceNumber) + 1);
        SelectLoadedMessage(messages.FirstOrDefault());
        Status = $"Loaded {messages.Count} {CreateBucketLabel(bucket)} message(s).";
        _addLog($"Loaded {messages.Count} {CreateBucketLabel(bucket)} message(s) from {entity.Metadata.Path}.");
        NotifyCommandStateChanged();
    }

    private static EntityAddress CreateEntityAddress(ServiceBusEntityNode entity)
    {
        return entity.Kind switch
        {
            EntityKind.Queue => new EntityAddress(EntityKind.Queue, entity.Name),
            EntityKind.Subscription => new EntityAddress(EntityKind.Subscription, entity.Name, entity.TopicName),
            EntityKind.Topic => new EntityAddress(EntityKind.Topic, entity.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(entity), "Unsupported entity kind.")
        };
    }

    private long? GetNextSequenceNumber(MessageBucket bucket)
    {
        return bucket == MessageBucket.Active ? _nextActiveSequenceNumber : _nextDeadLetterSequenceNumber;
    }

    private ObservableCollection<ExplorerMessage> GetMessageCollection(MessageBucket bucket)
    {
        return bucket == MessageBucket.Active ? ActiveMessages : DeadLetterMessages;
    }

    private void SetNextSequenceNumber(MessageBucket bucket, long? nextSequenceNumber)
    {
        if (bucket == MessageBucket.Active)
        {
            _nextActiveSequenceNumber = nextSequenceNumber;
        }
        else
        {
            _nextDeadLetterSequenceNumber = nextSequenceNumber;
        }
    }

    private void SelectLoadedMessage(ExplorerMessage? message)
    {
        _selectedDeadLetterMessage = null;
        _selectedDeadLetterMessages = [];
        SelectMessage(message);
    }

    private static string CreateBucketLabel(MessageBucket bucket)
    {
        return bucket == MessageBucket.Active ? "active" : "DLQ";
    }

    private void NotifyCommandStateChanged()
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        PeekActiveMessagesCommand.NotifyCanExecuteChanged();
        PeekNextActiveMessagesCommand.NotifyCanExecuteChanged();
        PeekDeadLetterMessagesCommand.NotifyCanExecuteChanged();
        PeekNextDeadLetterMessagesCommand.NotifyCanExecuteChanged();
        ReplayDeadLetterCommand.NotifyCanExecuteChanged();
        EditAndReplayDeadLetterCommand.NotifyCanExecuteChanged();
        DeleteSelectedDeadLetterCommand.NotifyCanExecuteChanged();
        DeleteVisibleDeadLetterCommand.NotifyCanExecuteChanged();
    }

    private bool CanSendMessage()
    {
        return IsConnected
            && !IsShellBusy
            && !IsBusy
            && _selectedEntity is { Kind: EntityKind.Queue or EntityKind.Topic };
    }

    private bool CanPeekMessages()
    {
        return IsConnected && !IsShellBusy && !IsBusy && CanInspectSelectedEntity();
    }

    private bool CanInspectSelectedEntity()
    {
        return _selectedEntity is { Kind: EntityKind.Queue or EntityKind.Subscription };
    }

    private bool CanPeekNextActiveMessages()
    {
        return CanPeekMessages() && _nextActiveSequenceNumber is not null;
    }

    private bool CanPeekNextDeadLetterMessages()
    {
        return CanPeekMessages() && _nextDeadLetterSequenceNumber is not null;
    }

    private bool CanUseSelectedDeadLetterMessage()
    {
        return CanPeekMessages() && _selectedDeadLetterMessages.Count == 1;
    }

    private bool CanDeleteSelectedDeadLetterMessages()
    {
        return CanPeekMessages() && _selectedDeadLetterMessages.Count > 0;
    }

    private bool CanDeleteVisibleDeadLetterMessages()
    {
        return CanPeekMessages() && DeadLetterMessages.Count > 0;
    }
}
