using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class MessageSearchWorkflow : ObservableObject
{
    private BrokerSession? session;
    private DeliverySearch? search;
    private CancellationTokenSource? operationCancellation;
    private MessageSearchQuery? query;
    private WorkspacePreferences preferences = new();
    private EntityAddress? scope;
    private MessageRow? focusedMessage;
    private string queryText = string.Empty;
    private string queryError = string.Empty;
    private string status = string.Empty;
    private string countSummary = string.Empty;
    private bool active;
    private bool busy;
    private bool complete;
    private bool discoveryReady;
    private bool discoveryComplete;
    private bool discoveredScanComplete;
    private bool defaultMessageId;
    private long generation;
    private long version;
    private int scannedDeliveries;
    private readonly List<MessageDelivery> accumulated = [];
    private readonly Dictionary<EntityAddress, EntityObservation> discovered = [];
    private readonly List<string> discoveryIssues = [];
    private readonly List<DeliverySearchSourceFailure> sourceFailures = [];
    private readonly Dictionary<DeliveryIdentity, MessageRow> rows = [];

    public ObservableCollection<EntityNode> Roots { get; } = [];
    public ObservableCollection<MessageRow> Messages { get; } = [];

    public bool IsActive { get => active; private set => SetProperty(ref active, value); }
    public bool IsComplete => complete;
    public bool IsBusy { get => busy; private set { if (SetProperty(ref busy, value)) OnPropertyChanged(nameof(CanContinue)); } }
    public bool CanContinue => IsActive
        && query is not null
        && !IsBusy
        && !complete
        && !(discoveredScanComplete && (!discoveryComplete || discoveryIssues.Count > 0));
    public string QueryText { get => queryText; private set => SetProperty(ref queryText, value); }
    public bool DefaultMessageId => defaultMessageId;
    public long Revision => version;
    public string QueryError { get => queryError; private set => SetProperty(ref queryError, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string CountSummary { get => countSummary; private set => SetProperty(ref countSummary, value); }
    public int SelectedCount => Messages.Count(row => row.IsSelected);
    public MessageRow? FocusedMessage { get => focusedMessage; set => SetProperty(ref focusedMessage, value); }

    public void SetSession(BrokerSession? value, long connectionGeneration)
    {
        Stop();
        version++;
        operationCancellation = null;
        IsBusy = false;
        session = value;
        generation = connectionGeneration;
        search = value is null ? null : new DeliverySearch(value.Messages);
        ClearResults();
        query = null;
        discoveryReady = false;
        QueryText = string.Empty;
        QueryError = string.Empty;
        IsActive = false;
        complete = false;
        Status = value is null ? string.Empty : "Ready to search this connection.";
        NotifySearchState();
    }

    public void SetPreferences(WorkspacePreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        preferences = value;
        foreach (MessageRow row in rows.Values)
        {
            row.SetTimeDisplay(value.TimestampDisplay);
        }
    }

    public async Task StartAsync(string queryText, bool defaultMessageId = false)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        QueryText = queryText;
        QueryError = string.Empty;

        if (!MessageSearchQuery.TryParse(queryText, out MessageSearchQuery? parsed, out string error))
        {
            Stop();
            version++;
            operationCancellation = null;
            query = null;
            IsActive = true;
            complete = false;
            IsBusy = false;
            ClearResults();
            QueryError = error;
            Status = $"Invalid search · {error}";
            NotifySearchState();
            return;
        }

        if (session is null || search is null)
        {
            Stop();
            version++;
            operationCancellation = null;
            query = parsed;
            IsActive = false;
            complete = false;
            IsBusy = false;
            ClearResults();
            Status = "Connect to a namespace before searching.";
            NotifySearchState();
            return;
        }

        BeginNewOperation();
        query = parsed;
        this.defaultMessageId = defaultMessageId;
        IsActive = true;
        complete = false;
        discoveryReady = false;
        ClearResults();
        IsBusy = true;
        Status = "Discovering receiving sources…";
        NotifySearchState();

        long operationVersion = version;
        BrokerSession operationSession = session;
        CancellationTokenSource operation = operationCancellation!;
        using CancellationTokenSource totalBudget = CreateBudgetCancellation(operation.Token);
        long budgetStartedAt = Stopwatch.GetTimestamp();
        try
        {
            if (!await DiscoverAndConfigureAsync(operationVersion, operationSession, totalBudget.Token)) return;
            Status = "Searching…";
            await ScanAndPublishAsync(
                operationVersion,
                operationSession,
                totalBudget.Token,
                RemainingBudget(budgetStartedAt),
                totalBudget,
                operation);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(operationVersion, operationSession))
            {
                Status = totalBudget.IsCancellationRequested && !operation.IsCancellationRequested
                    ? "Search paused at the time limit. Partial results; Continue retries source discovery."
                    : "Search stopped.";
                NotifySearchState();
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(operationVersion, operationSession))
            {
                Status = $"Search failed ({exception.GetType().Name}).";
                NotifySearchState();
            }
        }
        finally
        {
            if (IsCurrent(operationVersion, operationSession))
            {
                IsBusy = false;
                operationCancellation = null;
                NotifySearchState();
            }

            operation.Dispose();
        }
    }

    public Task RefreshAsync() => StartAsync(QueryText, defaultMessageId);

    public async Task ContinueAsync()
    {
        if (!CanContinue || session is null || search is null || query is null)
        {
            return;
        }

        BeginNewOperation();
        IsBusy = true;
        Status = "Continuing search…";
        NotifySearchState();

        long operationVersion = version;
        BrokerSession operationSession = session;
        CancellationTokenSource operation = operationCancellation!;
        using CancellationTokenSource totalBudget = CreateBudgetCancellation(operation.Token);
        long budgetStartedAt = Stopwatch.GetTimestamp();
        bool needsDiscovery = !discoveryReady;
        try
        {
            if (needsDiscovery)
            {
                Status = "Retrying source discovery…";
                if (!await DiscoverAndConfigureAsync(operationVersion, operationSession, totalBudget.Token)) return;
            }

            await ScanAndPublishAsync(
                operationVersion,
                operationSession,
                totalBudget.Token,
                needsDiscovery ? RemainingBudget(budgetStartedAt) : TimeSpan.FromSeconds(Math.Max(1, preferences.SearchTimeBudgetSeconds)),
                totalBudget,
                operation);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(operationVersion, operationSession))
            {
                Status = totalBudget.IsCancellationRequested && !operation.IsCancellationRequested
                    ? "Search paused at the time limit. Partial results; Continue retries source discovery."
                    : "Search stopped.";
                NotifySearchState();
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(operationVersion, operationSession))
            {
                Status = $"Search failed ({exception.GetType().Name}).";
                NotifySearchState();
            }
        }
        finally
        {
            if (IsCurrent(operationVersion, operationSession))
            {
                IsBusy = false;
                operationCancellation = null;
                NotifySearchState();
            }

            operation.Dispose();
        }
    }

    public void Stop() => operationCancellation?.Cancel();

    public void Clear()
    {
        Stop();
        version++;
        query = null;
        QueryText = string.Empty;
        QueryError = string.Empty;
        IsActive = false;
        complete = false;
        discoveryReady = false;
        IsBusy = false;
        operationCancellation = null;
        ClearResults();
        Status = string.Empty;
        NotifySearchState();
    }

    public void SelectScope(EntityNode? node)
    {
        scope = node?.Address;
        RebuildMessages();
        NotifySearchState();
    }

    public void SetAllChecked(bool value)
    {
        foreach (MessageRow row in Messages)
        {
            row.IsSelected = value;
        }

        OnPropertyChanged(nameof(SelectedCount));
    }

    private async Task<EntityDiscoverySnapshot> DiscoverAsync(BrokerSession operationSession, CancellationToken cancellationToken) =>
        await operationSession.Browser.DiscoverAsync(cancellationToken);

    private async Task<bool> DiscoverAndConfigureAsync(
        long operationVersion,
        BrokerSession operationSession,
        CancellationToken cancellationToken)
    {
        EntityDiscoverySnapshot snapshot = await DiscoverAsync(operationSession, cancellationToken);
        if (!IsCurrent(operationVersion, operationSession)) return false;

        ApplyDiscovery(snapshot);
        search!.Reset(generation, discovered.Keys.ToArray(), query!, defaultMessageId);
        discoveryReady = true;
        return true;
    }

    private async Task ScanAndPublishAsync(
        long operationVersion,
        BrokerSession operationSession,
        CancellationToken cancellationToken,
        TimeSpan timeBudget,
        CancellationTokenSource? totalBudget = null,
        CancellationTokenSource? operation = null)
    {
        DeliverySearchResult result = await search!.ScanNextAsync(
            Math.Max(1, preferences.SearchDeliveryBudget),
            timeBudget,
            cancellationToken);
        if (!IsCurrent(operationVersion, operationSession)) return;

        sourceFailures.Clear();
        sourceFailures.AddRange(result.SourceFailures);
        scannedDeliveries += result.ScannedDeliveries;

        var known = accumulated.Select(item => item.Identity).ToHashSet();
        foreach (MessageDelivery delivery in result.Matches)
        {
            if (known.Add(delivery.Identity)) accumulated.Add(delivery);
        }

        complete = result.IsComplete && discoveryComplete && discoveryIssues.Count == 0;
        discoveredScanComplete = result.IsComplete;
        RebuildPresentation();
        Status = totalBudget?.IsCancellationRequested == true
            && operation?.IsCancellationRequested != true
            && !result.IsComplete
            ? "Search paused at the time limit. Partial results; Continue scans another batch."
            : CreateStatus(result);
        NotifySearchState();
    }

    private void ApplyDiscovery(EntityDiscoverySnapshot snapshot)
    {
        discovered.Clear();
        discoveryIssues.Clear();
        discoveryComplete = snapshot.IsComplete;
        discoveryIssues.AddRange(snapshot.Issues);
        foreach (EntityObservation observation in snapshot.Entities)
        {
            discovered[new EntityAddress(observation.Entity.Kind, observation.Entity.Name, observation.Entity.TopicName)] = observation;
        }
    }

    private void RebuildPresentation()
    {
        RebuildMessages();
        RebuildRoots();
    }

    private void RebuildMessages()
    {
        var visible = accumulated.Where(IsInScope).ToList();
        MessageRow? previousFocus = FocusedMessage;
        Messages.Clear();
        foreach (MessageDelivery delivery in visible)
        {
            if (rows.TryGetValue(delivery.Identity, out MessageRow? row))
            {
                row.UpdateDelivery(delivery);
            }
            else
            {
                row = new MessageRow(delivery, preferences.TimestampDisplay);
                rows.Add(delivery.Identity, row);
                row.PropertyChanged += MessageRow_PropertyChanged;
            }

            Messages.Add(row);
        }

        FocusedMessage = previousFocus is not null && Messages.Contains(previousFocus)
            ? previousFocus
            : Messages.FirstOrDefault();
        CountSummary = $"{Messages.Count:N0} matches · {scannedDeliveries:N0} scanned";
    }

    private void RebuildRoots()
    {
        var existingNodes = Roots
            .SelectMany(root => root.Children.SelectMany(child => new[] { child }.Concat(child.Children)))
            .Where(node => node.Address is not null)
            .GroupBy(node => node.Address!)
            .ToDictionary(group => group.Key, group => group.First());
        EntityNode queues = Roots.FirstOrDefault(root => root.Name == "Queues") ?? new EntityNode("Queues", "Group");
        EntityNode topics = Roots.FirstOrDefault(root => root.Name == "Topics") ?? new EntityNode("Topics", "Group");
        var queueChildren = new List<EntityNode>();
        var topicChildren = new List<EntityNode>();
        var byAddress = accumulated
            .GroupBy(delivery => delivery.Identity.Source)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (EntityAddress address in byAddress.Keys.Where(address => address.Kind == EntityKind.Queue))
        {
            if (discovered.TryGetValue(address, out EntityObservation? observation))
            {
                queueChildren.Add(ReconcileNode(
                    existingNodes,
                    new EntityAddress(EntityKind.Queue, observation.Entity.Name),
                    observation,
                    byAddress[address]));
            }
        }

        foreach (IGrouping<string?, KeyValuePair<EntityAddress, List<MessageDelivery>>> topicGroup in byAddress
            .Where(item => item.Key.Kind == EntityKind.Subscription)
            .GroupBy(item => item.Key.TopicName, StringComparer.OrdinalIgnoreCase))
        {
            if (topicGroup.Key is null) continue;
            EntityObservation? topicObservation = discovered.Values.FirstOrDefault(item =>
                item.Entity.Kind == EntityKind.Topic
                && string.Equals(item.Entity.Name, topicGroup.Key, StringComparison.OrdinalIgnoreCase));
            if (topicObservation is null) continue;

            EntityAddress topicAddress = new(EntityKind.Topic, topicObservation.Entity.Name);
            EntityNode topicNode = ReconcileNode(
                existingNodes,
                topicAddress,
                topicObservation,
                topicGroup.SelectMany(item => item.Value).ToList());
            var subscriptionChildren = new List<EntityNode>();
            foreach (KeyValuePair<EntityAddress, List<MessageDelivery>> subscription in topicGroup)
            {
                if (discovered.TryGetValue(subscription.Key, out EntityObservation? observation))
                {
                    subscriptionChildren.Add(ReconcileNode(
                        existingNodes,
                        subscription.Key,
                        observation,
                        subscription.Value));
                }
            }

            ReconcileChildren(topicNode.Children, subscriptionChildren);
            if (topicNode.Children.Count > 0) topicChildren.Add(topicNode);
        }

        ReconcileChildren(queues.Children, queueChildren);
        ReconcileChildren(topics.Children, topicChildren);
        var desiredRoots = new[] { queues, topics }.Where(root => root.Children.Count > 0).ToList();
        ReconcileChildren(Roots, desiredRoots);
    }

    private static EntityNode ReconcileNode(
        IReadOnlyDictionary<EntityAddress, EntityNode> existingNodes,
        EntityAddress address,
        EntityObservation observation,
        IReadOnlyList<MessageDelivery> matches)
    {
        if (existingNodes.TryGetValue(address, out EntityNode? existing))
        {
            existing.UpdateObservation(WithMatchCounts(observation, matches));
            return existing;
        }

        return new EntityNode(
            observation.Entity.Name,
            observation.Entity.Kind.ToString(),
            WithMatchCounts(observation, matches));
    }

    private static void ReconcileChildren(
        ObservableCollection<EntityNode> target,
        IReadOnlyList<EntityNode> desired)
    {
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index])) target.RemoveAt(index);
        }

        for (int index = 0; index < desired.Count; index++)
        {
            EntityNode node = desired[index];
            int currentIndex = target.IndexOf(node);
            if (currentIndex < 0)
            {
                target.Insert(index, node);
            }
            else if (currentIndex != index)
            {
                target.Move(currentIndex, index);
            }
        }
    }

    private void MessageRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageRow.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    private bool IsInScope(MessageDelivery delivery)
    {
        if (scope is null) return true;
        EntityAddress source = delivery.Identity.Source;
        return source == scope
            || (scope.Kind == EntityKind.Topic
                && source.Kind == EntityKind.Subscription
                && string.Equals(source.TopicName, scope.Name, StringComparison.OrdinalIgnoreCase))
            || (scope.Kind == EntityKind.Queue && source == scope);
    }

    private static EntityObservation WithMatchCounts(
        EntityObservation original,
        IReadOnlyList<MessageDelivery> matches)
    {
        long active = matches.LongCount(item => item.Identity.Bucket == MessageBucket.Active);
        long deadLetter = matches.LongCount(item => item.Identity.Bucket == MessageBucket.DeadLetter);
        static CountObservation Match(long value) => new(value, CountAvailability.Known, "Matches in this search.");
        return original with
        {
            Counts = new EntityCountObservation(
                Match(active),
                Match(deadLetter),
                new(null, CountAvailability.NotSupported, "Scheduled counts are not part of search results."))
        };
    }

    private string CreateStatus(DeliverySearchResult result)
    {
        if (result.IsComplete && discoveryComplete && discoveryIssues.Count == 0)
        {
            return $"Search complete · {scannedDeliveries:N0} deliveries scanned.";
        }

        string partial = result.SourceFailures.Count > 0
            ? " Partial results; unavailable sources can be retried."
            : " Partial results; Continue scans another batch.";
        if (result.IsComplete && (!discoveryComplete || discoveryIssues.Count > 0))
        {
            return "Partial results; namespace discovery was incomplete. Start a new search to retry.";
        }

        return result.StopReason switch
        {
            DeliverySearchStopReason.MaxDeliveries => $"Search paused at the delivery limit.{partial}",
            DeliverySearchStopReason.TimeBudget => $"Search paused at the time limit.{partial}",
            DeliverySearchStopReason.Canceled => $"Search stopped; results are retained.{partial}",
            DeliverySearchStopReason.SourceFailure => $"Search paused with unavailable sources.{partial}",
            DeliverySearchStopReason.Superseded => "Search superseded by a newer request.",
            _ => $"Search is incomplete.{partial}"
        };
    }

    private void BeginNewOperation()
    {
        Stop();
        version++;
        operationCancellation = new CancellationTokenSource();
    }

    private CancellationTokenSource CreateBudgetCancellation(CancellationToken operationToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, preferences.SearchTimeBudgetSeconds)));
        return cancellation;
    }

    private TimeSpan RemainingBudget(long startedAt)
    {
        TimeSpan budget = TimeSpan.FromSeconds(Math.Max(1, preferences.SearchTimeBudgetSeconds));
        TimeSpan remaining = budget - Stopwatch.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
    }

    private bool IsCurrent(long operationVersion, BrokerSession operationSession) =>
        version == operationVersion && ReferenceEquals(session, operationSession);

    private void ClearResults()
    {
        accumulated.Clear();
        discovered.Clear();
        discoveryIssues.Clear();
        discoveryComplete = false;
        sourceFailures.Clear();
        scannedDeliveries = 0;
        discoveredScanComplete = false;
        scope = null;
        Messages.Clear();
        foreach (MessageRow row in rows.Values)
        {
            row.PropertyChanged -= MessageRow_PropertyChanged;
        }
        rows.Clear();
        Roots.Clear();
        FocusedMessage = null;
        CountSummary = string.Empty;
    }

    private void NotifySearchState()
    {
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(SelectedCount));
    }
}
