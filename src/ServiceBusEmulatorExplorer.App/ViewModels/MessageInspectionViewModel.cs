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
    private IReadOnlyList<ServiceBusEntityNode> _selectedTopicSubscriptions = [];
    private ExplorerMessage? _selectedDeadLetterMessage;
    private IReadOnlyList<ExplorerMessage> _selectedDeadLetterMessages = [];
    private readonly Dictionary<EntityAddress, CachedMessagePages> _cachedMessages = [];
    private readonly Dictionary<EntityAddress, long?> _nextTopicActiveSequenceNumbers = [];
    private readonly Dictionary<EntityAddress, long?> _nextTopicDeadLetterSequenceNumbers = [];
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
    private int _selectedMessageTabIndex = 1;
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

    public int SelectedMessageTabIndex
    {
        get => _selectedMessageTabIndex;
        set
        {
            if (SetProperty(ref _selectedMessageTabIndex, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public bool CanShowSendMessageCommand => _selectedEntity is { Kind: EntityKind.Queue or EntityKind.Topic };

    public string SendMessageCommandToolTip => CreateSendMessageToolTip();

    public bool CanShowPeekMessageCommands => _selectedEntity is { Kind: EntityKind.Queue or EntityKind.Subscription or EntityKind.Topic };

    public string PeekActiveMessagesCommandToolTip => CreatePeekMessagesToolTip("Load the first page of active messages.");

    public string PeekNextActiveMessagesCommandToolTip => CreatePeekNextMessagesToolTip(
        "Load the next page of active messages.",
        MessageBucket.Active,
        _nextActiveSequenceNumber);

    public string PeekDeadLetterMessagesCommandToolTip => CreatePeekMessagesToolTip("Load the first page of dead-letter messages.");

    public string PeekNextDeadLetterMessagesCommandToolTip => CreatePeekNextMessagesToolTip(
        "Load the next page of dead-letter messages.",
        MessageBucket.DeadLetter,
        _nextDeadLetterSequenceNumber);

    public bool CanShowDeadLetterCommands => CanUseSelectedEntityForDeadLetterOperations() && IsDeadLetterContextActive();

    public string ReplayDeadLetterCommandToolTip => CreateSelectedDeadLetterToolTip("Replay the selected DLQ message as a new active message.");

    public string EditAndReplayDeadLetterCommandToolTip => CreateSelectedDeadLetterToolTip("Edit the selected DLQ message before replaying it as a new active message.");

    public string DeleteSelectedDeadLetterCommandToolTip => CreateDeleteSelectedDeadLetterToolTip();

    public string DeleteVisibleDeadLetterCommandToolTip => CreateDeleteVisibleDeadLetterToolTip();

    public event EventHandler<IReadOnlyList<MessageCountUpdate>>? MessageCountsLoaded;

    public void SelectEntity(ServiceBusEntityNode? entity)
    {
        SelectEntity(entity, []);
    }

    public void SelectEntity(
        ServiceBusEntityNode? entity,
        IReadOnlyList<ServiceBusEntityNode> topicSubscriptions)
    {
        _selectedEntity = entity;
        _selectedTopicSubscriptions = entity is { Kind: EntityKind.Topic }
            ? topicSubscriptions
            : [];
        _selectionVersion++;
        Reset();

        if (entity is not null)
        {
            Status = entity.Kind == EntityKind.Topic
                ? CreateTopicStatus()
                : "Ready to peek active or DLQ messages.";
            ApplyCachedMessages(entity);
        }

        NotifyCommandStateChanged();
    }

    private string CreateTopicStatus()
    {
        return _selectedTopicSubscriptions.Count == 0
            ? "Selected topic has no subscriptions to inspect."
            : $"Ready to peek active or DLQ messages across {_selectedTopicSubscriptions.Count} subscription(s).";
    }

    public void CacheMessages(
        ServiceBusEntityNode entity,
        IReadOnlyList<ExplorerMessage> activeMessages,
        IReadOnlyList<ExplorerMessage> deadLetterMessages)
    {
        if (!CanInspectEntity(entity))
        {
            return;
        }

        EntityAddress address = CreateEntityAddress(entity);
        _cachedMessages[address] = new CachedMessagePages(activeMessages, deadLetterMessages);

        if (_selectedEntity == entity || IsCachedSubscriptionForSelectedTopic(entity))
        {
            ApplyCachedMessages(_selectedEntity);
            NotifyCommandStateChanged();
        }
    }

    private bool IsCachedSubscriptionForSelectedTopic(ServiceBusEntityNode entity)
    {
        return _selectedEntity is { Kind: EntityKind.Topic } selectedTopic
            && entity.Kind == EntityKind.Subscription
            && string.Equals(entity.TopicName, selectedTopic.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyCachedMessages(ServiceBusEntityNode? entity)
    {
        if (entity is null)
        {
            return;
        }

        if (entity.Kind == EntityKind.Topic)
        {
            ApplyCachedTopicMessages(entity);
            return;
        }

        if (!_cachedMessages.TryGetValue(CreateEntityAddress(entity), out CachedMessagePages? cachedMessages))
        {
            return;
        }

        ApplyCachedMessagePage(ActiveMessages, cachedMessages.ActiveMessages);
        ApplyCachedMessagePage(DeadLetterMessages, cachedMessages.DeadLetterMessages);
        _nextActiveSequenceNumber = GetNextSequenceNumber(cachedMessages.ActiveMessages);
        _nextDeadLetterSequenceNumber = GetNextSequenceNumber(cachedMessages.DeadLetterMessages);
        SelectLoadedMessage(cachedMessages.ActiveMessages.FirstOrDefault() ?? cachedMessages.DeadLetterMessages.FirstOrDefault());
        Status = $"Loaded cached first pages for {entity.Metadata.Path}.";
    }

    private void ApplyCachedTopicMessages(ServiceBusEntityNode topic)
    {
        List<ExplorerMessage> activeMessages = [];
        List<ExplorerMessage> deadLetterMessages = [];
        int cachedSubscriptionCount = 0;
        _nextTopicActiveSequenceNumbers.Clear();
        _nextTopicDeadLetterSequenceNumbers.Clear();

        foreach (ServiceBusEntityNode subscription in _selectedTopicSubscriptions)
        {
            EntityAddress address = CreateEntityAddress(subscription);
            if (!_cachedMessages.TryGetValue(address, out CachedMessagePages? cachedMessages))
            {
                continue;
            }

            cachedSubscriptionCount++;
            activeMessages.AddRange(AddSourceProperties(cachedMessages.ActiveMessages, subscription));
            deadLetterMessages.AddRange(AddSourceProperties(cachedMessages.DeadLetterMessages, subscription));
            _nextTopicActiveSequenceNumbers[address] = GetNextSequenceNumber(cachedMessages.ActiveMessages);
            _nextTopicDeadLetterSequenceNumbers[address] = GetNextSequenceNumber(cachedMessages.DeadLetterMessages);
        }

        if (cachedSubscriptionCount == 0)
        {
            return;
        }

        ApplyCachedMessagePage(ActiveMessages, activeMessages);
        ApplyCachedMessagePage(DeadLetterMessages, deadLetterMessages);
        SelectLoadedMessage(activeMessages.FirstOrDefault() ?? deadLetterMessages.FirstOrDefault());
        Status = $"Loaded cached first pages for {cachedSubscriptionCount} subscription(s) under {topic.Name}.";
    }

    private static void ApplyCachedMessagePage(
        ObservableCollection<ExplorerMessage> target,
        IReadOnlyList<ExplorerMessage> messages)
    {
        target.Clear();
        foreach (ExplorerMessage message in messages)
        {
            target.Add(message);
        }
    }

    private static long? GetNextSequenceNumber(IReadOnlyList<ExplorerMessage> messages)
    {
        return messages.Count == 0 ? null : messages.Max(message => message.SequenceNumber) + 1;
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
        _nextTopicActiveSequenceNumbers.Clear();
        _nextTopicDeadLetterSequenceNumbers.Clear();
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

        if (entity.Kind == EntityKind.Topic)
        {
            await PeekTopicSubscriptionMessagesAsync(bucket, resetPage, selectionVersion, cancellationToken);
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
        PublishMessageCountUpdate(new MessageCountUpdate(address, bucket, messages.Count));
        SelectLoadedMessage(messages.FirstOrDefault());
        Status = $"Loaded {messages.Count} {CreateBucketLabel(bucket)} message(s).";
        _addLog($"Loaded {messages.Count} {CreateBucketLabel(bucket)} message(s) from {entity.Metadata.Path}.");
        NotifyCommandStateChanged();
    }

    private async Task PeekTopicSubscriptionMessagesAsync(
        MessageBucket bucket,
        bool resetPage,
        int selectionVersion,
        CancellationToken cancellationToken)
    {
        ServiceBusEntityNode? topic = _selectedEntity;
        if (topic is not { Kind: EntityKind.Topic })
        {
            return;
        }

        if (_selectedTopicSubscriptions.Count == 0)
        {
            Status = "Selected topic has no subscriptions to inspect.";
            NotifyCommandStateChanged();
            return;
        }

        if (resetPage)
        {
            ClearTopicNextSequenceNumbers(bucket);
        }

        List<ExplorerMessage> messages = [];
        List<MessageCountUpdate> countUpdates = [];
        int failureCount = 0;

        foreach (ServiceBusEntityNode subscription in _selectedTopicSubscriptions)
        {
            EntityAddress address = CreateEntityAddress(subscription);
            long? fromSequenceNumber = resetPage ? null : GetTopicNextSequenceNumber(bucket, address);
            if (!resetPage && fromSequenceNumber is null)
            {
                continue;
            }

            try
            {
                IReadOnlyList<ExplorerMessage> subscriptionMessages = await _messageService.PeekMessagesAsync(
                    address,
                    bucket,
                    take: 50,
                    fromSequenceNumber,
                    cancellationToken);
                if (selectionVersion != _selectionVersion)
                {
                    return;
                }

                messages.AddRange(AddSourceProperties(subscriptionMessages, subscription));
                SetTopicNextSequenceNumber(bucket, address, GetNextSequenceNumber(subscriptionMessages));
                CacheTopicSubscriptionPage(subscription, bucket, subscriptionMessages);
                countUpdates.Add(new MessageCountUpdate(address, bucket, subscriptionMessages.Count));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failureCount++;
                SetTopicNextSequenceNumber(bucket, address, null);
                _addLog($"Load {CreateBucketLabel(bucket)} messages from {subscription.Metadata.Path} failed: {ex.Message}");
            }
        }

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

        PublishMessageCountUpdates(countUpdates);
        SelectLoadedMessage(messages.FirstOrDefault());
        Status = failureCount == 0
            ? $"Loaded {messages.Count} {CreateBucketLabel(bucket)} message(s) from {topic.Name} subscriptions."
            : $"Loaded {messages.Count} {CreateBucketLabel(bucket)} message(s) from {topic.Name} subscriptions; {failureCount} subscription(s) failed.";
        _addLog(Status);
        NotifyCommandStateChanged();
    }

    private void CacheTopicSubscriptionPage(
        ServiceBusEntityNode subscription,
        MessageBucket bucket,
        IReadOnlyList<ExplorerMessage> messages)
    {
        EntityAddress address = CreateEntityAddress(subscription);
        _cachedMessages.TryGetValue(address, out CachedMessagePages? cachedMessages);
        IReadOnlyList<ExplorerMessage> activeMessages = bucket == MessageBucket.Active
            ? messages
            : cachedMessages?.ActiveMessages ?? [];
        IReadOnlyList<ExplorerMessage> deadLetterMessages = bucket == MessageBucket.DeadLetter
            ? messages
            : cachedMessages?.DeadLetterMessages ?? [];

        _cachedMessages[address] = new CachedMessagePages(activeMessages, deadLetterMessages);
    }

    private static IEnumerable<ExplorerMessage> AddSourceProperties(
        IReadOnlyList<ExplorerMessage> messages,
        ServiceBusEntityNode subscription)
    {
        return messages.Select(message => AddSourceProperties(message, subscription));
    }

    private static ExplorerMessage AddSourceProperties(
        ExplorerMessage message,
        ServiceBusEntityNode subscription)
    {
        Dictionary<string, object?> systemProperties = new(message.SystemProperties)
        {
            ["SourceSubscription"] = subscription.Metadata.Path
        };

        return message with { SystemProperties = systemProperties };
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

    private void ClearTopicNextSequenceNumbers(MessageBucket bucket)
    {
        if (bucket == MessageBucket.Active)
        {
            _nextTopicActiveSequenceNumbers.Clear();
        }
        else
        {
            _nextTopicDeadLetterSequenceNumbers.Clear();
        }
    }

    private long? GetTopicNextSequenceNumber(MessageBucket bucket, EntityAddress address)
    {
        Dictionary<EntityAddress, long?> sequenceNumbers = GetTopicNextSequenceNumbers(bucket);
        return sequenceNumbers.TryGetValue(address, out long? nextSequenceNumber)
            ? nextSequenceNumber
            : null;
    }

    private void SetTopicNextSequenceNumber(MessageBucket bucket, EntityAddress address, long? nextSequenceNumber)
    {
        GetTopicNextSequenceNumbers(bucket)[address] = nextSequenceNumber;
    }

    private Dictionary<EntityAddress, long?> GetTopicNextSequenceNumbers(MessageBucket bucket)
    {
        return bucket == MessageBucket.Active
            ? _nextTopicActiveSequenceNumbers
            : _nextTopicDeadLetterSequenceNumbers;
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

    private void PublishMessageCountUpdate(MessageCountUpdate update)
    {
        PublishMessageCountUpdates([update]);
    }

    private void PublishMessageCountUpdates(IReadOnlyList<MessageCountUpdate> updates)
    {
        if (updates.Count > 0)
        {
            MessageCountsLoaded?.Invoke(this, updates);
        }
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
        OnPropertyChanged(nameof(CanShowSendMessageCommand));
        OnPropertyChanged(nameof(SendMessageCommandToolTip));
        OnPropertyChanged(nameof(CanShowPeekMessageCommands));
        OnPropertyChanged(nameof(PeekActiveMessagesCommandToolTip));
        OnPropertyChanged(nameof(PeekNextActiveMessagesCommandToolTip));
        OnPropertyChanged(nameof(PeekDeadLetterMessagesCommandToolTip));
        OnPropertyChanged(nameof(PeekNextDeadLetterMessagesCommandToolTip));
        OnPropertyChanged(nameof(CanShowDeadLetterCommands));
        OnPropertyChanged(nameof(ReplayDeadLetterCommandToolTip));
        OnPropertyChanged(nameof(EditAndReplayDeadLetterCommandToolTip));
        OnPropertyChanged(nameof(DeleteSelectedDeadLetterCommandToolTip));
        OnPropertyChanged(nameof(DeleteVisibleDeadLetterCommandToolTip));
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
        return _selectedEntity switch
        {
            { Kind: EntityKind.Topic } => _selectedTopicSubscriptions.Count > 0,
            not null => CanInspectEntity(_selectedEntity),
            _ => false
        };
    }

    private bool IsDeadLetterContextActive()
    {
        return SelectedMessageTabIndex == 2;
    }

    private static bool CanInspectEntity(ServiceBusEntityNode entity)
    {
        return entity.Kind is EntityKind.Queue or EntityKind.Subscription;
    }

    private bool CanUseSelectedEntityForDeadLetterOperations()
    {
        return _selectedEntity is not null && CanInspectEntity(_selectedEntity);
    }

    private bool CanPeekNextActiveMessages()
    {
        return CanPeekMessages() && HasNextSequenceNumber(MessageBucket.Active);
    }

    private bool CanPeekNextDeadLetterMessages()
    {
        return CanPeekMessages() && HasNextSequenceNumber(MessageBucket.DeadLetter);
    }

    private bool HasNextSequenceNumber(MessageBucket bucket)
    {
        if (_selectedEntity is { Kind: EntityKind.Topic })
        {
            return GetTopicNextSequenceNumbers(bucket).Values.Any(nextSequenceNumber => nextSequenceNumber is not null);
        }

        return GetNextSequenceNumber(bucket) is not null;
    }

    private bool CanUseSelectedDeadLetterMessage()
    {
        return IsConnected
            && !IsShellBusy
            && !IsBusy
            && CanUseSelectedEntityForDeadLetterOperations()
            && _selectedDeadLetterMessages.Count == 1;
    }

    private bool CanDeleteSelectedDeadLetterMessages()
    {
        return IsConnected
            && !IsShellBusy
            && !IsBusy
            && CanUseSelectedEntityForDeadLetterOperations()
            && _selectedDeadLetterMessages.Count > 0;
    }

    private bool CanDeleteVisibleDeadLetterMessages()
    {
        return IsConnected
            && !IsShellBusy
            && !IsBusy
            && CanUseSelectedEntityForDeadLetterOperations()
            && DeadLetterMessages.Count > 0;
    }

    private string CreateSendMessageToolTip()
    {
        const string purpose = "Send a brand-new message to the selected queue or topic.";
        return CreateToolTip(purpose, GetConnectionOrBusyReason());
    }

    private string CreatePeekMessagesToolTip(string purpose)
    {
        return CreateToolTip(purpose, GetPeekUnavailableReason());
    }

    private string CreatePeekNextMessagesToolTip(
        string purpose,
        MessageBucket bucket,
        long? nextSequenceNumber)
    {
        string? reason = GetPeekUnavailableReason();
        if (reason is null && !HasNextSequenceNumberForSelectedEntity(bucket, nextSequenceNumber))
        {
            reason = "Load the first page before requesting the next page.";
        }

        return CreateToolTip(purpose, reason);
    }

    private bool HasNextSequenceNumberForSelectedEntity(
        MessageBucket bucket,
        long? nextSequenceNumber)
    {
        if (_selectedEntity is { Kind: EntityKind.Topic })
        {
            return HasNextSequenceNumber(bucket);
        }

        return nextSequenceNumber is not null;
    }

    private string CreateSelectedDeadLetterToolTip(string purpose)
    {
        string? reason = GetConnectionOrBusyReason();
        if (reason is null && _selectedDeadLetterMessages.Count != 1)
        {
            reason = "Select exactly one DLQ message first.";
        }

        return CreateToolTip(purpose, reason);
    }

    private string CreateDeleteSelectedDeadLetterToolTip()
    {
        const string purpose = "Delete selected DLQ messages after explicit confirmation.";
        string? reason = GetConnectionOrBusyReason();
        if (reason is null && _selectedDeadLetterMessages.Count == 0)
        {
            reason = "Select one or more DLQ messages first.";
        }

        return CreateToolTip(purpose, reason);
    }

    private string CreateDeleteVisibleDeadLetterToolTip()
    {
        const string purpose = "Delete every DLQ message currently visible after typed confirmation.";
        string? reason = GetConnectionOrBusyReason();
        if (reason is null && DeadLetterMessages.Count == 0)
        {
            reason = "Load DLQ messages before deleting the visible page.";
        }

        return CreateToolTip(purpose, reason);
    }

    private string? GetConnectionOrBusyReason()
    {
        if (!IsConnected)
        {
            return "Connect to an emulator first.";
        }

        if (IsShellBusy)
        {
            return "Wait for the current shell operation to finish.";
        }

        return IsBusy ? "Wait for the current message operation to finish." : null;
    }

    private string? GetPeekUnavailableReason()
    {
        string? reason = GetConnectionOrBusyReason();
        if (reason is not null)
        {
            return reason;
        }

        return _selectedEntity is { Kind: EntityKind.Topic } && _selectedTopicSubscriptions.Count == 0
            ? "Selected topic has no subscriptions."
            : null;
    }

    private static string CreateToolTip(string purpose, string? unavailableReason)
    {
        return unavailableReason is null ? purpose : $"{purpose} Unavailable: {unavailableReason}";
    }

    private sealed record CachedMessagePages(
        IReadOnlyList<ExplorerMessage> ActiveMessages,
        IReadOnlyList<ExplorerMessage> DeadLetterMessages);
}

public sealed record MessageCountUpdate(
    EntityAddress Address,
    MessageBucket Bucket,
    long Count);
