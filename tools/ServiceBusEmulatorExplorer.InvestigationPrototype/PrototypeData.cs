using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public sealed class EntityNode : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Kind { get; init; }
    public bool IsGroup { get; init; }
    public ObservableCollection<EntityNode> Children { get; } = [];
    public string MessageCount { get; set; } = "";
    public string DlqCount { get; set; } = "";
    private bool isExpanded = true;
    public bool IsExpanded { get => isExpanded; set { isExpanded = value; Changed(); } }
    private bool isVisible = true;
    public bool IsVisible { get => isVisible; set { isVisible = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyCounts() { Changed(nameof(MessageCount)); Changed(nameof(DlqCount)); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MessageRow : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string EventName { get; init; }
    public required string MessageId { get; init; }
    public string CorrelationId { get; init; } = "";
    public bool IsDeadLetter { get; init; }
    public string? DeadLetterReason { get; init; }
    public string StateLabel => IsDeadLetter ? "DLQ" : "Active";
    public string OriginalMessageId { get; init; } = "";
    public int ReplayNumber { get; init; }
    public required string Source { get; init; }
    public string SourceName => Source.Split('/')[^1];
    public required string Body { get; init; }
    public required string Properties { get; init; }
    public DateTime Enqueued { get; init; }
    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class PrototypeData
{
    public static ObservableCollection<EntityNode> CreateTree()
    {
        var queues = new EntityNode { Name = "Queues", Path = "queues", Kind = "Group", IsGroup = true };
        queues.Children.Add(Entity("webhook-delivery", "webhook-delivery", "Queue", "12", "0"));
        queues.Children.Add(Entity("audit-events", "audit-events", "Queue", "—", "—"));
        queues.Children.Add(Entity("empty-queue", "empty-queue", "Queue", "0", "0"));
        var topics = new EntityNode { Name = "Topics", Path = "topics", Kind = "Group", IsGroup = true };
        var orders = Entity("order-events", "order-events", "Topic", "240", "5");
        orders.Children.Add(Entity("billing", "order-events/billing", "Subscription", "120", "3"));
        orders.Children.Add(Entity("analytics", "order-events/analytics", "Subscription", "80", "0"));
        orders.Children.Add(Entity("notifications", "order-events/notifications", "Subscription", "40", "2"));
        topics.Children.Add(orders);
        var inventory = Entity("inventory-events", "inventory-events", "Topic", "18", "0");
        inventory.IsExpanded = false;
        inventory.Children.Add(Entity("stock-updates", "inventory-events/stock-updates", "Subscription", "18", "0"));
        topics.Children.Add(inventory);
        return [queues, topics];
    }

    private static EntityNode Entity(string name, string path, string kind, string active, string dlq) =>
        new() { Name = name, Path = path, Kind = kind, MessageCount = active, DlqCount = dlq };

    public static Dictionary<string, List<MessageRow>> CreateMessages(IEnumerable<EntityNode> roots)
    {
        var result = new Dictionary<string, List<MessageRow>>();
        foreach (var node in roots.SelectMany(Flatten).Where(n => !n.IsGroup && n.Kind != "Topic"))
        {
            var count = int.TryParse(node.MessageCount, out var active) ? active : 9;
            var dead = int.TryParse(node.DlqCount, out var dlq) ? dlq : 1;
            result[node.Path] = Enumerable.Range(0, count).Select(i => CreateMessage(node.Path, i, false)).ToList();
            result[node.Path + "/$deadletter"] = Enumerable.Range(0, dead).Select(i => CreateMessage(node.Path, i, true)).ToList();
        }
        return result;
    }

    public static IEnumerable<EntityNode> Flatten(EntityNode node)
    {
        yield return node;
        foreach (var descendant in node.Children.SelectMany(Flatten)) yield return descendant;
    }

    public static MessageRow CreateMessage(string source, int index, bool deadLetter, bool incoming = false)
    {
        var names = new[] { "OrderDispatched", "OrderCreated", "PaymentCaptured", "OrderUpdated", "ShipmentDelivered" };
        var name = names[index % names.Length];
        var id = $"evt-{index + 1042:D6}";
        var correlationId = index % 7 == 0 ? "checkout-80341"
            : index % 7 == 1 ? "checkout-80342"
            : index % 7 == 2 ? "payment-90817"
            : index % 7 == 3 ? "shipment-55109"
            : $"order-{202600 + index}";
        var body = JsonSerializer.Serialize(new
        {
            specVersion = "1.0",
            eventType = name,
            source = "retail-ordering",
            data = new
            {
                orderId = $"ORD-{202600 + index}",
                shipmentId = "SHIP-55109",
                carrier = "DHL",
                destination = "Krakow, PL",
                occurredAtUtc = "2026-09-11T09:42:18Z",
                items = index == 2
                    ? Enumerable.Range(1, 18).Select(n => new { sku = $"SKU-{n:D4}", quantity = n, description = "Extended nested payload example for inspecting long event bodies and horizontal wrapping." }).ToArray()
                    : [new { sku = "SKU-1084", quantity = 2, description = "Wireless keyboard" }]
            }
        });
        if (source == "audit-events" && index == 0) body = "{\"eventType\": \"AuditRecorded\", \"data\": [ incomplete JSON";
        if (source == "audit-events" && index == 1) body = "Audit service started.\nThis is a plain-text event body, not JSON.\nCorrelation: audit-local-0042";
        return new MessageRow
        {
            Key = $"{source}/{(deadLetter ? "dlq" : "active")}/{id}", EventName = name,
            MessageId = id, OriginalMessageId = id, CorrelationId = correlationId,
            IsDeadLetter = deadLetter, DeadLetterReason = deadLetter ? "MaxDeliveryCountExceeded" : null,
            Source = source, Body = body,
            Enqueued = incoming ? DateTime.UtcNow : new DateTime(2026, 9, 11, 9, 42, 18, DateTimeKind.Utc).AddSeconds(-index * 23),
            Properties = JsonSerializer.Serialize(new
            {
                messageId = id, correlationId, contentType = "application/json",
                source, sequenceNumber = 2048 + index, deliveryCount = deadLetter ? 10 : 0,
                deadLetterReason = deadLetter ? "MaxDeliveryCountExceeded" : null,
                deadLetterErrorDescription = deadLetter ? "Consumer could not process this event after 10 deliveries. Synthetic diagnostic example." : null,
                applicationProperties = new { environment = "local", producer = "order-service", schemaVersion = 1 }
            }, new JsonSerializerOptions { WriteIndented = true })
        };
    }
}
