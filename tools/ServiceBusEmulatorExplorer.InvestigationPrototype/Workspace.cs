using System.Collections.ObjectModel;
using System.ComponentModel;

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
    public bool CanLoadMore => IsConnected && Messages.Count < CurrentRows().Count();
    public string Footer { get; private set; } = "";
    public string Status { get; private set; } = "Synthetic data · no Azure connection";
    public MessageRow? FocusedMessage
    {
        get => focusedMessage;
        set { focusedMessage = value; PropertyChanged?.Invoke(this, new(nameof(FocusedMessage))); }
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
