using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class MessageBrowseWorkflow : ObservableObject
{
    private BrokerSession? session;
    private DeliveryPager? pager;
    private CancellationTokenSource? readCancellation;
    private long generation;
    private readonly HashSet<DeliveryIdentity> deleted = [];
    private int version;
    private bool busy;
    private EntityNode? selectedEntity;
    private MessageRow? focusedMessage;
    private MessageBucket bucket;
    private string entityFilter = string.Empty;
    private WorkspacePreferences preferences = new();
    public ObservableCollection<EntityNode> Roots { get; } = [];
    public ObservableCollection<MessageRow> Messages { get; } = [];
    public EntityNode? SelectedEntity => selectedEntity;
    public bool IsConnected => session is not null;
    public MessageRow? FocusedMessage
    {
        get => focusedMessage;
        set => SetProperty(ref focusedMessage, value);
    }
    public bool IsBusy { get => busy; private set => SetProperty(ref busy, value); }
    public bool IsDeadLetter => bucket == MessageBucket.DeadLetter;
    public bool CanLoadMore => session is not null && selectedEntity is not null && !busy && (pager?.HasMore ?? false);
    public string EntityPath => selectedEntity?.Path ?? "Select an entity";
    public int SelectedCount => Messages.Count(row => row.IsSelected);
    public string CountSummary => selectedEntity is null ? "" :
        $"{Messages.Count} loaded · {selectedEntity.DisplayMessageCount} Active · {selectedEntity.DisplayScheduledCount} Scheduled · {selectedEntity.DisplayDlqCount} DLQ";
    public bool ShowsSource => selectedEntity?.Kind == nameof(EntityKind.Topic);

    public void SetPreferences(WorkspacePreferences value)
    {
        preferences = value;
        foreach (var row in Messages) row.SetTimeDisplay(value.TimestampDisplay);
    }

    public void SetSession(BrokerSession? value, long connectionGeneration)
    {
        Cancel();
        session = value;
        OnPropertyChanged(nameof(IsConnected));
        generation = connectionGeneration;
        deleted.Clear();
        selectedEntity = null;
        pager = null;
        FocusedMessage = null;
        ReplaceMessages([]);
        Roots.Clear();
        if (value is not null) ApplySnapshot(value.Snapshot);
        NotifyScope();
    }

    public void ApplySnapshot(EntityDiscoverySnapshot snapshot)
    {
        var previousAddress = selectedEntity?.Address;
        var existingNodes = AllEntities()
            .Where(node => node.Address is not null)
            .GroupBy(node => node.Address!)
            .ToDictionary(group => group.Key, group => group.First());
        var observations = snapshot.Entities.ToList();
        if (!snapshot.IsComplete)
        {
            var observedAddresses = observations
                .Select(item => new EntityAddress(item.Entity.Kind, item.Entity.Name, item.Entity.TopicName))
                .ToHashSet();
            observations.AddRange(existingNodes.Values
                .Where(node => node.Observation is not null && !observedAddresses.Contains(node.Address!))
                .Select(node => CreateStaleObservation(node.Observation!)));
        }
        var queues = ReuseGroup("Queues");
        var topics = ReuseGroup("Topics");
        foreach (var item in observations.Where(item => item.Entity.Kind == EntityKind.Queue))
            queues.Children.Add(ReconcileNode(item, existingNodes));
        foreach (var item in observations.Where(item => item.Entity.Kind == EntityKind.Topic))
        {
            var topic = ReconcileNode(item, existingNodes);
            topic.Children.Clear();
            foreach (var child in observations.Where(child => child.Entity.Kind == EntityKind.Subscription &&
                string.Equals(child.Entity.TopicName, item.Entity.Name, StringComparison.OrdinalIgnoreCase)))
                topic.Children.Add(ReconcileNode(child, existingNodes));
            topics.Children.Add(topic);
        }
        Roots.Clear();
        Roots.Add(queues);
        Roots.Add(topics);
        selectedEntity = AllEntities().FirstOrDefault(node => node.Address == previousAddress);
        ApplyEntityFilter();
        NotifyScope();
    }

    private static EntityObservation CreateStaleObservation(EntityObservation previous)
    {
        const string detail = "Discovery incomplete; this entity was not returned by the latest discovery.";
        static CountObservation Stale(string detail) => new(null, CountAvailability.Stale, detail);
        return previous with
        {
            Counts = new EntityCountObservation(Stale(detail), Stale(detail), Stale(detail))
        };
    }

    public IEnumerable<EntityNode> AllEntities() => Roots.SelectMany(root => root.Children)
        .SelectMany(node => new[] { node }.Concat(node.Children));

    public void FilterEntities(string text)
    {
        entityFilter = text ?? string.Empty;
        ApplyEntityFilter();
    }

    private void ApplyEntityFilter()
    {
        string text = entityFilter;
        foreach (var root in Roots)
        {
            foreach (var node in root.Children)
            {
                bool matches = node.Path.Contains(text, StringComparison.OrdinalIgnoreCase);
                foreach (var child in node.Children)
                    child.IsVisible = matches || child.Path.Contains(text, StringComparison.OrdinalIgnoreCase);
                node.IsVisible = matches || node.Children.Any(child => child.IsVisible);
            }
            root.IsVisible = root.Children.Any(node => node.IsVisible);
        }
    }

    private EntityNode ReconcileNode(EntityObservation observation, IReadOnlyDictionary<EntityAddress, EntityNode> existingNodes)
    {
        EntityAddress address = new(observation.Entity.Kind, observation.Entity.Name, observation.Entity.TopicName);
        if (existingNodes.TryGetValue(address, out EntityNode? existing))
        {
            existing.UpdateObservation(observation);
            return existing;
        }

        return new EntityNode(observation.Entity.Name, observation.Entity.Kind.ToString(), observation);
    }

    private EntityNode ReuseGroup(string name)
    {
        EntityNode? existing = Roots.FirstOrDefault(root => root.IsGroup && root.Name == name);
        var group = new EntityNode(name, "Group");
        if (existing is not null)
        {
            group.IsExpanded = existing.IsExpanded;
            group.IsVisible = existing.IsVisible;
        }
        return group;
    }

    public async Task SelectAsync(EntityNode node, bool deadLetter)
    {
        if (session is null || node.IsGroup) return;
        Cancel();
        selectedEntity = node;
        bucket = deadLetter ? MessageBucket.DeadLetter : MessageBucket.Active;
        FocusedMessage = null;
        ReplaceMessages([]);
        pager = CreatePager();
        NotifyScope();
        await LoadMoreAsync();
    }

    public async Task OpenKnownAsync(MessageRow known)
    {
        if (session is null || known.Key.ConnectionGeneration != generation || deleted.Contains(known.Key)) return;
        EntityNode? entity = AllEntities().FirstOrDefault(node => node.Address == known.Key.Source);
        if (entity is null) return;
        MessageRow? current = Messages.FirstOrDefault(row => row.Key == known.Key);
        if (current is not null && selectedEntity == entity && IsDeadLetter == known.IsDeadLetter)
        {
            FocusedMessage = current;
            return;
        }
        Task selection = SelectAsync(entity, known.IsDeadLetter);
        int selectionVersion = version;
        await selection;
        if (known.Key.ConnectionGeneration != generation || selectedEntity != entity || version != selectionVersion || deleted.Contains(known.Key)) return;
        MessageRow? observed = Messages.FirstOrDefault(row => row.Key == known.Key);
        if (observed is null)
        {
            observed = new MessageRow(known.Delivery, preferences.TimestampDisplay);
            observed.MarkRetainedAsUnobserved();
            AddRow(observed);
            NotifyScope();
        }
        FocusedMessage = observed;
    }

    private DeliveryPager CreatePager()
    {
        var result = new DeliveryPager(session!.Messages);
        var sources = selectedEntity!.Kind == nameof(EntityKind.Topic)
            ? selectedEntity.Children.Select(child => child.Address!).ToArray()
            : new[] { selectedEntity.Address! };
        result.Reset(generation, sources, bucket);
        return result;
    }

    public async Task LoadMoreAsync()
    {
        if (session is null || pager is null || busy) return;
        var operationVersion = ++version;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        readCancellation = cancellation;
        IsBusy = true;
        NotifyScope();
        try
        {
            var deliveries = await pager.LoadNextAsync(PageSize(), cancellation.Token);
            if (operationVersion != version) return;
            var keys = Messages.Select(row => row.Key).ToHashSet();
            foreach (var delivery in deliveries)
                if (keys.Add(delivery.Identity)) AddRow(new(delivery, preferences.TimestampDisplay));
            FocusedMessage ??= Messages.FirstOrDefault();
        }
        finally
        {
            if (operationVersion == version)
            {
                IsBusy = false;
                readCancellation = null;
                NotifyScope();
            }
        }
    }

    private int PageSize() => selectedEntity?.Kind switch
    {
        nameof(EntityKind.Topic) => preferences.TopicPageSize,
        nameof(EntityKind.Subscription) => preferences.SubscriptionPageSize,
        _ => preferences.QueuePageSize
    };

    public async Task RefreshAsync()
    {
        if (session is null || busy) return;
        var operationVersion = ++version;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        readCancellation = cancellation;
        IsBusy = true;
        NotifyScope();
        try
        {
            var snapshot = await session.Browser.DiscoverAsync(cancellation.Token);
            if (operationVersion != version) return;
            var address = selectedEntity?.Address;
            var target = Math.Max(PageSize(), Messages.Count);
            ApplySnapshot(snapshot);
            if (selectedEntity is null || address is null)
            {
                pager = null;
                FocusedMessage = null;
                ReplaceMessages([]);
                NotifyScope();
                return;
            }
            var refreshedPager = CreatePager();
            var deliveries = new List<MessageDelivery>();
            while (deliveries.Count < target && refreshedPager.HasMore)
            {
                var page = await refreshedPager.LoadNextAsync(Math.Min(200, target - deliveries.Count), cancellation.Token);
                deliveries.AddRange(page);
                if (page.Count == 0) break;
            }
            if (operationVersion != version) return;
            pager = refreshedPager;
            MessageRow? oldFocused = FocusedMessage;
            DeliveryIdentity? oldFocusedKey = oldFocused?.Key;
            var oldRows = Messages.ToDictionary(row => row.Key);
            var rows = new List<MessageRow>(deliveries.Count + Messages.Count);
            var observedKeys = new HashSet<DeliveryIdentity>();
            foreach (MessageDelivery delivery in deliveries)
            {
                if (deleted.Contains(delivery.Identity)) continue;
                observedKeys.Add(delivery.Identity);
                if (oldRows.TryGetValue(delivery.Identity, out MessageRow? existing))
                {
                    existing.UpdateDelivery(delivery);
                    rows.Add(existing);
                }
                else
                {
                    rows.Add(new MessageRow(delivery, preferences.TimestampDisplay));
                }
            }

            foreach (MessageRow row in Messages)
            {
                if (!observedKeys.Contains(row.Key) && (row.IsSelected || ReferenceEquals(row, oldFocused)))
                {
                    row.MarkRetainedAsUnobserved();
                    rows.Add(row);
                }
            }

            ReconcileMessages(rows);
            FocusedMessage = oldFocusedKey is not null
                ? Messages.FirstOrDefault(row => row.Key == oldFocusedKey) ?? Messages.FirstOrDefault()
                : Messages.FirstOrDefault();
        }
        finally
        {
            if (operationVersion == version)
            {
                IsBusy = false;
                readCancellation = null;
                NotifyScope();
            }
        }
    }

    public void SetAllChecked(bool value)
    {
        foreach (var row in Messages) row.IsSelected = value;
    }

    public void ForgetDeleted(IReadOnlySet<DeliveryIdentity> identities)
    {
        deleted.UnionWith(identities.Where(identity => identity.ConnectionGeneration == generation));
        foreach (var row in Messages.Where(row => deleted.Contains(row.Key)).ToArray())
        {
            row.PropertyChanged -= RowChanged;
            Messages.Remove(row);
        }
        if (FocusedMessage is not null && deleted.Contains(FocusedMessage.Key)) FocusedMessage = Messages.FirstOrDefault();
        NotifyScope();
    }

    public void Cancel()
    {
        ++version;
        readCancellation?.Cancel();
        readCancellation = null;
        IsBusy = false;
        NotifyScope();
    }

    private void ReplaceMessages(IEnumerable<MessageRow> rows)
    {
        foreach (var row in Messages) row.PropertyChanged -= RowChanged;
        Messages.Clear();
        foreach (var row in rows) AddRow(row);
    }

    private void ReconcileMessages(IReadOnlyList<MessageRow> desiredRows)
    {
        for (int index = 0; index < desiredRows.Count; index++)
        {
            MessageRow desired = desiredRows[index];
            if (index < Messages.Count && ReferenceEquals(Messages[index], desired))
                continue;

            int existingIndex = -1;
            for (int candidate = index + 1; candidate < Messages.Count; candidate++)
            {
                if (ReferenceEquals(Messages[candidate], desired))
                {
                    existingIndex = candidate;
                    break;
                }
            }

            if (existingIndex >= 0)
                Messages.Move(existingIndex, index);
            else
            {
                desired.PropertyChanged += RowChanged;
                Messages.Insert(index, desired);
            }
        }

        while (Messages.Count > desiredRows.Count)
        {
            MessageRow removed = Messages[^1];
            removed.PropertyChanged -= RowChanged;
            Messages.RemoveAt(Messages.Count - 1);
        }
    }

    private void AddRow(MessageRow row)
    {
        if (deleted.Contains(row.Key)) return;
        row.PropertyChanged += RowChanged;
        Messages.Add(row);
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageRow.IsSelected)) OnPropertyChanged(nameof(SelectedCount));
    }

    private void NotifyScope()
    {
        OnPropertyChanged(nameof(SelectedEntity));
        OnPropertyChanged(nameof(EntityPath));
        OnPropertyChanged(nameof(IsDeadLetter));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(ShowsSource));
    }
}
