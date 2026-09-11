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
    public IReadOnlyList<MessageRow> ReplayTargets => !IsConnected || !IsDeadLetter ? []
        : Messages.Any(row => row.IsSelected) ? Messages.Where(row => row.IsSelected).ToArray()
        : FocusedMessage is { } focused ? [focused] : [];
    public bool CanReplay => ReplayTargets.Count > 0;
    public bool CanEditAndReplay => ReplayTargets.Count == 1;
    public bool CanLoadMore => IsConnected && Messages.Count < CurrentRows().Count();
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
        if (node.IsGroup || !IsConnected || SelectedEntity == node) return;
        ClearSelection();
        SelectedEntity = node;
        visibleLimit = 50;
        pendingIncoming = 0;
        Refresh();
    }

    private void ClearSelection()
    {
        foreach (var row in Messages) row.IsSelected = false;
        FocusedMessage = null;
    }

    public void SetDeadLetter(bool value)
    {
        if (IsDeadLetter == value) return;
        ClearSelection();
        IsDeadLetter = value;
        visibleLimit = 50;
        pendingIncoming = 0;
        Refresh();
    }

    public void Refresh()
    {
        if (!IsConnected || SelectedEntity is null) return;
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
        visibleLimit += 50;
        Refresh();
    }

    public int Replay(string? editedBody = null, string? newMessageId = null)
    {
        var targets = ReplayTargets;
        if (targets.Count == 0) return 0;
        if (targets.Count != 1 && (editedBody is not null || newMessageId is not null))
            throw new ArgumentException("Select exactly one message to edit and replay.");

        // Prepare every copy before changing fixtures so validation failure cannot partially replay a batch.
        var entities = Roots.SelectMany(PrototypeData.Flatten).ToList();
        var existingIds = fixtures.Values.SelectMany(rows => rows).Select(row => row.MessageId).ToHashSet(StringComparer.Ordinal);
        var copies = new List<(EntityNode Destination, EntityNode? Topic, MessageRow Row)>();
        var replayIds = new List<string>();
        var sequence = incomingSequence;
        foreach (var original in targets)
        {
            var id = newMessageId ?? Guid.NewGuid().ToString();
            if (string.IsNullOrWhiteSpace(id) || id == original.MessageId || !existingIds.Add(id))
                throw new ArgumentException("Use a new, non-empty message ID that is not already in the sample data.");
            var source = entities.Single(node => node.Path == original.Source);
            var topic = source.Kind == "Subscription"
                ? entities.Single(node => node.Kind == "Topic" && node.Children.Contains(source)) : null;
            var destinations = topic is null ? new[] { source } : topic.Children.ToArray();
            foreach (var destination in destinations)
            {
                if (!fixtures.ContainsKey(destination.Path))
                    throw new InvalidOperationException("The replay destination is unavailable.");
                copies.Add((destination, topic, CreateReplayCopy(original, destination.Path, id, editedBody, sequence++)));
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
        Refresh();
        Status = $"Simulated replay of {targets.Count} DLQ message{(targets.Count == 1 ? "" : "s")} · New ID{(targets.Count == 1 ? "" : "s")}: {string.Join(", ", replayIds)} · Originals remain in DLQ";
        Changed();
        return targets.Count;
    }

    public string? ValidateReplayId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "Enter a new message ID.";
        if (fixtures.Values.SelectMany(rows => rows).Any(row => row.MessageId == id))
            return "This message ID already exists. Enter a different message ID.";
        return null;
    }

    private static MessageRow CreateReplayCopy(MessageRow original, string destination, string id, string? editedBody, int sequence)
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

    public void SetSearch(string text)
    {
        foreach (var root in Roots) Filter(root, text.Trim());
    }

    private static bool Filter(EntityNode node, string search)
    {
        var childMatches = node.Children.Select(child => Filter(child, search)).ToArray();
        node.IsVisible = search.Length == 0 || node.Path.Contains(search, StringComparison.OrdinalIgnoreCase) || childMatches.Any(match => match);
        if (search.Length > 0 && childMatches.Any(match => match)) node.IsExpanded = true;
        return node.IsVisible;
    }

    public void NotifySelectionChanged() => Changed();
    private void RowChanged(object? sender, PropertyChangedEventArgs args) => NotifySelectionChanged();

    public void SimulateIncoming()
    {
        if (!IsConnected || SelectedEntity is null) return;
        var source = SelectedEntity.Kind == "Topic" ? SelectedEntity.Children[0] : SelectedEntity;
        var row = PrototypeData.CreateMessage(source.Path, incomingSequence++, IsDeadLetter, incoming: true);
        row.PropertyChanged += RowChanged;
        fixtures[source.Path + (IsDeadLetter ? "/$deadletter" : "")].Insert(0, row);
        IncrementCount(source);
        var parent = Roots.SelectMany(PrototypeData.Flatten).FirstOrDefault(node => node.Children.Contains(source) && node.Kind == "Topic");
        if (parent is not null) IncrementCount(parent);
        pendingIncoming++;
        Status = $"{pendingIncoming} new message{(pendingIncoming == 1 ? "" : "s")} available · Refresh to load";
        Changed();
    }

    private void IncrementCount(EntityNode node)
    {
        if (IsDeadLetter && int.TryParse(node.DlqCount, out var dead)) node.DlqCount = (dead + 1).ToString();
        if (!IsDeadLetter && int.TryParse(node.MessageCount, out var active)) node.MessageCount = (active + 1).ToString();
        node.NotifyCounts();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new(string.Empty));
}
