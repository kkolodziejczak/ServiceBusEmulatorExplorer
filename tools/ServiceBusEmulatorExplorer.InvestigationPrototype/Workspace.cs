using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// Throwaway interaction model: synthetic events only, no Service Bus or persistence.
public sealed class Workspace : INotifyPropertyChanged
{
    private readonly Dictionary<string, List<MessageRow>> fixtures;
    private int visibleLimit = 50;
    private int incomingSequence = 10000;
    private int pendingIncoming;
    private bool changingChecks;
    private readonly Dictionary<string, int> replayNumbers = new(StringComparer.Ordinal);
    private readonly List<string> recentCorrelations = [];
    private List<MessageRow> searchSnapshot = [];
    private MessageSearchQuery? parsedSearch;
    private MessageRow? focusedMessage;
    public ObservableCollection<EntityNode> Roots { get; } = PrototypeData.CreateTree();
    public ObservableCollection<MessageRow> Messages { get; } = [];
    public EntityNode? SelectedEntity { get; private set; }
    public bool IsDeadLetter { get; private set; }
    public bool IsConnected { get; private set; } = true;
    public string ConnectionLabel => IsConnected ? "● Connected" : "○ Disconnected";
    public string EntityPath => SelectedEntity?.Path ?? "";
    public string EntityTitle => SelectedEntity?.Name ?? "Choose an entity";
    public int SelectedCount => Messages.Count(row => row.IsSelected);
    public IReadOnlyList<MessageRow> ReplayTargets => !IsConnected ? []
        : Messages.Any(row => row.IsSelected) ? Messages.Where(row => row.IsSelected).ToArray()
        : FocusedMessage is { } focused ? [focused] : [];
    public bool CanReplay => ReplayTargets.Count > 0 && ReplayTargets.All(row => row.IsDeadLetter);
    public bool CanEditAndReplay => CanReplay && ReplayTargets.Count == 1 && ReplayTargets[0] == FocusedMessage;
    public string NextReplayId => FocusedMessage is { IsDeadLetter: true } row
        ? NextId(row, replayNumbers, AllMessageIds()).Id : "";
    public bool CanLoadMore => IsConnected && !IsCorrelationSearch && Messages.Count < CurrentRows().Count();
    public bool IsCorrelationSearch { get; private set; }
    public bool SearchByMessageId { get; private set; }
    public string CorrelationQuery { get; private set; } = "";
    public string SearchStatus { get; private set; } = "";
    public string SearchQueryError { get; private set; } = "";
    public bool IsSearching { get; private set; }
    public bool SearchComplete { get; private set; }
    public int ScannedMessages { get; private set; }
    public int SearchTotalEntities => Roots.SelectMany(PrototypeData.Flatten).Count(node => !node.IsGroup && node.Kind != "Topic");
    public int SearchScannedEntities => SearchComplete ? SearchTotalEntities : searchSnapshot
        .Select((row, index) => (row.Source, Index: index)).GroupBy(item => item.Source)
        .Count(group => group.Max(item => item.Index) < ScannedMessages);
    public string Footer { get; private set; } = "";
    public string Status { get; private set; } = "Synthetic data · no Azure connection";
    public MessageRow? FocusedMessage
    {
        get => focusedMessage;
        set { focusedMessage = value; Changed(); }
    }

    public Workspace()
    {
        fixtures = PrototypeData.CreateMessages(Roots);
        foreach (var row in fixtures.Values.SelectMany(rows => rows)) row.PropertyChanged += RowChanged;
        SelectEntity(Roots.SelectMany(PrototypeData.Flatten).Single(node => node.Path == "order-events/billing"));
        foreach (var row in Messages.Take(2)) row.IsSelected = true;
    }

    public void SelectEntity(EntityNode node)
    {
        if (node.IsGroup || !IsConnected || (SelectedEntity == node && !IsCorrelationSearch)) return;
        ResetCorrelationSearch();
        ClearSelection();
        SelectedEntity = node;
        visibleLimit = 50;
        pendingIncoming = 0;
        Refresh();
    }

    private void ClearSelection()
    {
        SetAllChecked(false);
        FocusedMessage = null;
    }

    public void SetDeadLetter(bool value)
    {
        if (IsDeadLetter == value && !IsCorrelationSearch) return;
        ResetCorrelationSearch();
        ClearSelection();
        IsDeadLetter = value;
        visibleLimit = 50;
        pendingIncoming = 0;
        Refresh();
    }

    public void Refresh()
    {
        if (!IsConnected || SelectedEntity is null) return;
        if (IsCorrelationSearch) { StartSearch(CorrelationQuery, SearchByMessageId); return; }
        var focusedKey = FocusedMessage?.Key;
        var rows = CurrentRows().OrderByDescending(row => row.Enqueued).ThenBy(row => row.Key).ToList();
        var visible = rows.Take(visibleLimit).ToList();
        // A focused or checked row remains visible while new events arrive.
        foreach (var retained in rows.Where(row => row.Key == focusedKey || row.IsSelected))
            if (!visible.Contains(retained)) visible.Add(retained);
        // Reconcile in place so refresh doesn't reset list scroll/selection containers.
        for (var i = Messages.Count - 1; i >= 0; i--)
            if (!visible.Contains(Messages[i])) Messages.RemoveAt(i);
        for (var i = 0; i < visible.Count; i++)
        {
            var existing = Messages.IndexOf(visible[i]);
            if (existing < 0) Messages.Insert(i, visible[i]);
            else if (existing != i) Messages.Move(existing, i);
        }
        FocusedMessage = Messages.FirstOrDefault(row => row.Key == focusedKey) ?? Messages.FirstOrDefault();
        pendingIncoming = 0;
        var counts = SelectedEntity.MessageCount == "—" ? "Total unavailable"
            : $"{SelectedEntity.MessageCount} messages · {SelectedEntity.DlqCount} DLQ";
        Footer = $"{Messages.Count} loaded · {counts}";
        Status = rows.Count == 0 ? "No messages in this scope" : $"Updated {DateTime.UtcNow:HH:mm:ss} UTC · Sample data";
        Changed();
    }

    private IEnumerable<MessageRow> CurrentRows()
    {
        if (SelectedEntity is null) return [];
        var sources = SelectedEntity.Kind == "Topic" ? SelectedEntity.Children : new ObservableCollection<EntityNode> { SelectedEntity };
        return sources.SelectMany(node => fixtures[node.Path + (IsDeadLetter ? "/$deadletter" : "")]);
    }

    public void LoadMore()
    {
        if (!CanLoadMore) return;
        visibleLimit += 50;
        Refresh();
    }

    public IEnumerable<string> Suggestions(string text)
    {
        var query = text.Trim();
        return recentCorrelations.Concat(fixtures.Values.SelectMany(rows => rows).Select(row => row.CorrelationId))
            .Where(id => !string.IsNullOrEmpty(id) && id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal).Take(8).ToArray();
    }

    public IReadOnlyList<MessageRow> SnapshotMessages() => fixtures.Values.SelectMany(rows => rows).ToArray();

    public void StartCorrelationSearch(string query) => StartSearch(query, false);

    public void StartMessageIdSearch(string query) => StartSearch(query, true);

    private void StartSearch(string query, bool messageId)
    {
        if (!IsConnected) return;
        query = query.Trim();
        if (query.Length == 0) { ClearCorrelationSearch(); return; }
        ClearSelection();
        Messages.Clear();
        IsCorrelationSearch = true;
        SearchByMessageId = messageId;
        CorrelationQuery = query;
        ScannedMessages = 0;
        SearchComplete = false;
        IsSearching = false;
        searchSnapshot.Clear();
        if (!MessageSearchQuery.TryParse(query, out parsedSearch, out var error))
        {
            SearchQueryError = error;
            SearchStatus = $"Invalid search · {error}";
            Footer = "0 matches · Search not started";
            Status = SearchStatus;
            Changed();
            return;
        }
        SearchQueryError = "";
        if (!messageId && fixtures.Values.SelectMany(rows => rows).Any(row => row.CorrelationId == query))
        {
            recentCorrelations.Remove(query);
            recentCorrelations.Insert(0, query);
            if (recentCorrelations.Count > 12) recentCorrelations.RemoveAt(12);
        }
        searchSnapshot = fixtures.Values.SelectMany(rows => rows)
            .OrderByDescending(row => row.Enqueued).ThenBy(row => row.Key, StringComparer.Ordinal).ToList();
        ScannedMessages = 0;
        SearchComplete = false;
        IsSearching = true;
        UpdateSearchStatus();
    }

    public void ScanNext()
    {
        if (!IsSearching) return;
        var page = searchSnapshot.Skip(ScannedMessages).Take(50).ToArray();
        foreach (var row in page)
            if (parsedSearch!.Matches(row, SearchByMessageId)) Messages.Add(row);
        ScannedMessages += page.Length;
        if (ScannedMessages >= searchSnapshot.Count)
        {
            IsSearching = false;
            SearchComplete = true;
        }
        FocusedMessage ??= Messages.FirstOrDefault();
        UpdateSearchStatus();
    }

    public void StopCorrelationSearch()
    {
        if (!IsSearching) return;
        IsSearching = false;
        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        SearchStatus = IsSearching ? "Searching…"
            : SearchComplete ? "Search complete" : "Search incomplete";
        SearchStatus += $" · {SearchScannedEntities} of {SearchTotalEntities} entities scanned";
        Footer = $"{Messages.Count} match{(Messages.Count == 1 ? "" : "es")}{(SearchComplete ? "" : " so far")} · {ScannedMessages} scanned · {SearchStatus}";
        Status = $"{SearchStatus} · Case-sensitive {(SearchByMessageId ? "message" : "correlation")} ID · Sample data";
        Changed();
    }

    public void ClearCorrelationSearch()
    {
        if (!IsCorrelationSearch) return;
        ClearSelection();
        ResetCorrelationSearch();
        Messages.Clear();
        Refresh();
    }

    private void ResetCorrelationSearch()
    {
        if (IsCorrelationSearch)
            foreach (var node in Roots.SelectMany(PrototypeData.Flatten))
            {
                node.SetMatchingCounts(null, null);
                node.IsVisible = true;
            }
        IsCorrelationSearch = false;
        SearchByMessageId = false;
        CorrelationQuery = "";
        SearchStatus = "";
        SearchQueryError = "";
        parsedSearch = null;
        IsSearching = false;
        SearchComplete = false;
        ScannedMessages = 0;
        searchSnapshot.Clear();
    }

    public bool OpenMessage(MessageRow target)
    {
        if (!IsConnected) return false;
        var entity = Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => !node.IsGroup && node.Kind != "Topic" && node.Path == target.Source);
        if (entity is null || !fixtures.TryGetValue(target.Source + (target.IsDeadLetter ? "/$deadletter" : ""), out var rows)) return false;
        var stored = rows.FirstOrDefault(row => row.Key == target.Key);
        if (stored is null) return false;
        ClearSelection();
        ResetCorrelationSearch();
        SetSearch("");
        SelectedEntity = entity;
        IsDeadLetter = stored.IsDeadLetter;
        var position = rows.OrderByDescending(row => row.Enqueued).ThenBy(row => row.Key).ToList().IndexOf(stored);
        visibleLimit = ((position / 50) + 1) * 50;
        pendingIncoming = 0;
        foreach (var parent in Roots.SelectMany(PrototypeData.Flatten).Where(node => PrototypeData.Flatten(node).Contains(entity))) parent.IsExpanded = true;
        Refresh();
        FocusedMessage = stored;
        return true;
    }

    public int Replay(string? editedBody = null, string? newMessageId = null)
    {
        var targets = ReplayTargets;
        if (!CanReplay) return 0;
        if (editedBody is not null && !CanEditAndReplay)
            throw new ArgumentException("Focus the single checked message to edit and replay it.");
        if (targets.Count != 1 && (editedBody is not null || newMessageId is not null))
            throw new ArgumentException("Select exactly one message to edit and replay.");

        // Prepare every copy before changing fixtures so validation failure cannot partially replay a batch.
        var entities = Roots.SelectMany(PrototypeData.Flatten).ToList();
        var existingIds = AllMessageIds();
        var nextNumbers = new Dictionary<string, int>(replayNumbers, StringComparer.Ordinal);
        var copies = new List<(EntityNode Destination, EntityNode? Topic, MessageRow Row)>();
        var replayIds = new List<string>();
        var sequence = incomingSequence;
        foreach (var original in targets)
        {
            var next = NextId(original, nextNumbers, existingIds);
            var id = newMessageId ?? next.Id;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id == original.MessageId || !existingIds.Add(id))
                throw new ArgumentException("Use a new, non-empty message ID that is not already in the sample data.");
            nextNumbers[OriginalId(original)] = next.Number;
            var source = entities.Single(node => node.Path == original.Source);
            var topic = source.Kind == "Subscription"
                ? entities.Single(node => node.Kind == "Topic" && node.Children.Contains(source)) : null;
            var destinations = topic is null ? new[] { source } : topic.Children.ToArray();
            foreach (var destination in destinations)
            {
                if (!fixtures.ContainsKey(destination.Path))
                    throw new InvalidOperationException("The replay destination is unavailable.");
                copies.Add((destination, topic, CreateReplayCopy(original, destination.Path, id, next.Number, editedBody, sequence++)));
            }
            replayIds.Add(id);
        }

        foreach (var copy in copies)
        {
            copy.Row.PropertyChanged += RowChanged;
            fixtures[copy.Destination.Path].Insert(0, copy.Row);
            IncrementActiveCount(copy.Destination);
            if (copy.Topic is not null) IncrementActiveCount(copy.Topic);
        }
        incomingSequence = sequence;
        foreach (var pair in nextNumbers) replayNumbers[pair.Key] = pair.Value;
        // Keep a completed search and its current selection stable; a new search discovers the copies.
        if (!IsCorrelationSearch) Refresh();
        Status = $"Simulated replay of {targets.Count} DLQ message{(targets.Count == 1 ? "" : "s")} · New ID{(targets.Count == 1 ? "" : "s")}: {string.Join(", ", replayIds)} · Originals remain in DLQ";
        Changed();
        return targets.Count;
    }

    public int DeleteMessages(IReadOnlyList<MessageRow> targets)
    {
        if (!IsConnected) return 0;
        var removed = new HashSet<MessageRow>();
        var entities = Roots.SelectMany(PrototypeData.Flatten).ToArray();
        foreach (var target in targets)
        {
            if (!fixtures.TryGetValue(target.Source + (target.IsDeadLetter ? "/$deadletter" : ""), out var rows)) continue;
            var stored = rows.FirstOrDefault(row => row.Key == target.Key);
            if (stored is null || !removed.Add(stored)) continue;
            rows.Remove(stored);
            stored.PropertyChanged -= RowChanged;
            stored.IsSelected = false;
            var entity = entities.FirstOrDefault(node => !node.IsGroup && node.Path == stored.Source);
            if (entity is null) continue;
            DecrementCount(entity, stored.IsDeadLetter);
            var topic = entities.FirstOrDefault(node => node.Kind == "Topic" && node.Children.Contains(entity));
            if (topic is not null) DecrementCount(topic, stored.IsDeadLetter);
        }
        if (removed.Count == 0) return 0;
        if (IsCorrelationSearch)
        {
            // Keep the scan cursor aligned if a removed row was in an already scanned page.
            var removedBeforeCursor = searchSnapshot.Take(ScannedMessages).Count(removed.Contains);
            searchSnapshot.RemoveAll(removed.Contains);
            ScannedMessages -= removedBeforeCursor;
            foreach (var row in removed) Messages.Remove(row);
            if (FocusedMessage is null || removed.Contains(FocusedMessage)) FocusedMessage = Messages.FirstOrDefault();
            if (IsSearching && ScannedMessages >= searchSnapshot.Count)
            {
                IsSearching = false;
                SearchComplete = true;
            }
            if (SearchQueryError.Length == 0) UpdateSearchStatus();
        }
        else Refresh();
        Status = $"Simulated deletion of {removed.Count} message{(removed.Count == 1 ? "" : "s")} · Sample data";
        Changed();
        return removed.Count;
    }

    private static void DecrementCount(EntityNode node, bool deadLetter)
    {
        if (deadLetter && int.TryParse(node.DlqCount, out var dead)) node.DlqCount = Math.Max(0, dead - 1).ToString();
        if (!deadLetter && int.TryParse(node.MessageCount, out var active)) node.MessageCount = Math.Max(0, active - 1).ToString();
        node.NotifyCounts();
    }

    private HashSet<string> AllMessageIds() => fixtures.Values.SelectMany(rows => rows)
        .Select(row => row.MessageId).ToHashSet(StringComparer.Ordinal);

    private static string OriginalId(MessageRow row) => string.IsNullOrEmpty(row.OriginalMessageId) ? row.MessageId : row.OriginalMessageId;

    private static (string Id, int Number) NextId(MessageRow row, IReadOnlyDictionary<string, int> numbers, HashSet<string> existingIds)
    {
        var original = OriginalId(row);
        var number = Math.Max(numbers.GetValueOrDefault(original), row.ReplayNumber);
        string id;
        do
        {
            number++;
            var suffix = $"-replay-{number}";
            id = original[..Math.Min(original.Length, 128 - suffix.Length)] + suffix;
        } while (existingIds.Contains(id));
        return (id, number);
    }

    private static MessageRow CreateReplayCopy(MessageRow original, string destination, string id, int replayNumber, string? editedBody, int sequence)
    {
        var enqueued = DateTime.UtcNow;
        var properties = JsonNode.Parse(original.Properties)?.AsObject()
            ?? throw new InvalidOperationException("The source message properties are unavailable.");
        properties["messageId"] = id;
        properties["source"] = destination;
        properties["sequenceNumber"] = sequence;
        properties["enqueuedTimeUtc"] = enqueued;
        properties["deliveryCount"] = 0;
        properties["deadLetterReason"] = null;
        properties["deadLetterErrorDescription"] = null;
        return new MessageRow
        {
            Key = $"{destination}/active/{id}", EventName = original.EventName, MessageId = id,
            OriginalMessageId = OriginalId(original), ReplayNumber = replayNumber,
            CorrelationId = original.CorrelationId, IsDeadLetter = false,
            Source = destination, Body = editedBody ?? original.Body, Enqueued = enqueued,
            Properties = properties.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        };
    }

    private static void IncrementActiveCount(EntityNode node)
    {
        if (int.TryParse(node.MessageCount, out var count)) node.MessageCount = (count + 1).ToString();
        node.NotifyCounts();
    }

    public void ToggleConnection()
    {
        ResetCorrelationSearch();
        IsConnected = !IsConnected;
        ClearSelection();
        Messages.Clear();
        SelectedEntity = null;
        if (IsConnected)
        {
            IsDeadLetter = false;
            SelectEntity(Roots.SelectMany(PrototypeData.Flatten).Single(node => node.Path == "order-events/billing"));
        }
        else
        {
            Footer = "Disconnected · Connect to browse the demo";
            Status = "Disconnected · No network request was made";
            Changed();
        }
    }

    public void ResetConnectionSimulation()
    {
        if (IsConnected) throw new InvalidOperationException("Disconnect before selecting another sample connection.");
        ClearSelection();
        ResetCorrelationSearch();
        foreach (var row in fixtures.Values.SelectMany(rows => rows)) row.PropertyChanged -= RowChanged;
        Messages.Clear();
        fixtures.Clear();
        Roots.Clear();
        foreach (var root in PrototypeData.CreateTree()) Roots.Add(root);
        foreach (var pair in PrototypeData.CreateMessages(Roots)) fixtures.Add(pair.Key, pair.Value);
        foreach (var row in fixtures.Values.SelectMany(rows => rows)) row.PropertyChanged += RowChanged;
        replayNumbers.Clear();
        recentCorrelations.Clear();
        incomingSequence = 10000;
        pendingIncoming = 0;
        visibleLimit = 50;
        IsDeadLetter = false;
        SelectedEntity = null;
        Footer = "Disconnected · Connect to browse the demo";
        Status = "Sample connection changed · Connect to browse fresh sample data";
        Changed();
    }

    public void SetSearch(string text)
    {
        if (IsCorrelationSearch) { UpdateMatchingTree(); return; }
        foreach (var root in Roots) Filter(root, text.Trim());
    }

    private void UpdateMatchingTree()
    {
        var counts = Messages.GroupBy(row => row.Source).ToDictionary(group => group.Key,
            group => (Active: group.Count(row => !row.IsDeadLetter), DeadLetter: group.Count(row => row.IsDeadLetter)));
        foreach (var root in Roots) UpdateMatchingNode(root, counts);
    }

    private static (int Active, int DeadLetter) UpdateMatchingNode(EntityNode node, IReadOnlyDictionary<string, (int Active, int DeadLetter)> counts)
    {
        var count = counts.TryGetValue(node.Path, out var own) ? own : (Active: 0, DeadLetter: 0);
        foreach (var child in node.Children)
        {
            var childCount = UpdateMatchingNode(child, counts);
            count.Active += childCount.Active;
            count.DeadLetter += childCount.DeadLetter;
        }
        node.SetMatchingCounts(node.IsGroup ? null : count.Active, node.IsGroup ? null : count.DeadLetter);
        node.IsVisible = count.Active + count.DeadLetter > 0;
        if (node.IsVisible && node.Children.Count > 0) node.IsExpanded = true;
        return count;
    }

    private static bool Filter(EntityNode node, string search)
    {
        var childMatches = node.Children.Select(child => Filter(child, search)).ToArray();
        node.IsVisible = search.Length == 0 || node.Path.Contains(search, StringComparison.OrdinalIgnoreCase) || childMatches.Any(match => match);
        if (search.Length > 0 && childMatches.Any(match => match)) node.IsExpanded = true;
        return node.IsVisible;
    }

    public void SetAllChecked(bool value)
    {
        changingChecks = true;
        try { foreach (var row in Messages) row.IsSelected = value; }
        finally { changingChecks = false; }
        NotifySelectionChanged();
    }

    public void NotifySelectionChanged() => PropertyChanged?.Invoke(this, new(nameof(SelectedCount)));
    private void RowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!changingChecks) NotifySelectionChanged();
    }

    public void SimulateIncoming()
    {
        if (!IsConnected || SelectedEntity is null || IsCorrelationSearch) return;
        InjectSampleArrival(SelectedEntity.Path, IsDeadLetter);
        pendingIncoming++;
        Status = $"{pendingIncoming} new message{(pendingIncoming == 1 ? "" : "s")} available · Refresh to load";
        Changed();
    }

    public MessageRow InjectSampleArrival(string entityPath, bool deadLetter)
    {
        if (!IsConnected) throw new InvalidOperationException("Connect before simulating an arrival.");
        var entity = Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => !node.IsGroup && node.Path == entityPath)
            ?? throw new ArgumentException("The arrival destination is unavailable.", nameof(entityPath));
        var source = entity.Kind == "Topic" ? entity.Children.FirstOrDefault() : entity;
        if (source is null) throw new ArgumentException("The topic has no sample subscription.", nameof(entityPath));
        var destination = fixtures[source.Path + (deadLetter ? "/$deadletter" : "")];
        var existingIds = AllMessageIds();
        MessageRow row;
        do { row = PrototypeData.CreateMessage(source.Path, incomingSequence++, deadLetter, incoming: true); }
        while (existingIds.Contains(row.MessageId));
        row.PropertyChanged += RowChanged;
        destination.Insert(0, row);
        IncrementCount(source, deadLetter);
        var parent = Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => node.Children.Contains(source) && node.Kind == "Topic");
        if (parent is not null) IncrementCount(parent, deadLetter);
        Changed();
        return row;
    }

    private static void IncrementCount(EntityNode node, bool deadLetter)
    {
        if (deadLetter && int.TryParse(node.DlqCount, out var dead)) node.DlqCount = (dead + 1).ToString();
        if (!deadLetter && int.TryParse(node.MessageCount, out var active)) node.MessageCount = (active + 1).ToString();
        node.NotifyCounts();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed()
    {
        if (IsCorrelationSearch) UpdateMatchingTree();
        PropertyChanged?.Invoke(this, new(string.Empty));
    }
}
