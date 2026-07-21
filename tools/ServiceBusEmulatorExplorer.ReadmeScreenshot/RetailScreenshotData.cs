using System.Text;
using System.Text.Json;
using ServiceBusEmulatorExplorer.Core.Messaging;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class RetailScreenshotData
{
    public static readonly DateTimeOffset ScenarioTime = new(2026, 7, 21, 19, 36, 29, TimeSpan.Zero);

    public static IReadOnlyList<ServiceBusEntityNode> CreateEntities()
    {
        return
        [
            CreateEntity(EntityKind.Queue, "webhook-delivery", null, active: 2),
            CreateEntity(EntityKind.Topic, "inventory-events", null, active: 0),
            CreateEntity(EntityKind.Subscription, "replenishment", "inventory-events", active: 2),
            CreateEntity(EntityKind.Subscription, "warehouse-reservations", "inventory-events", active: 2),
            CreateEntity(EntityKind.Topic, "order-events", null, active: 0),
            CreateEntity(EntityKind.Subscription, "analytics", "order-events", active: 4),
            CreateEntity(EntityKind.Subscription, "billing", "order-events", active: 4),
            CreateEntity(EntityKind.Subscription, "customer-notifications", "order-events", active: 4),
            CreateEntity(EntityKind.Subscription, "fulfillment", "order-events", active: 4),
            CreateEntity(EntityKind.Topic, "payment-events", null, active: 0),
            CreateEntity(EntityKind.Subscription, "accounting", "payment-events", active: 2),
            CreateEntity(EntityKind.Subscription, "fraud-review", "payment-events", active: 2)
        ];
    }

    private static ServiceBusEntityNode CreateEntity(
        EntityKind kind,
        string name,
        string? topicName,
        long active)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            active,
            DeadLetterMessageCount: 0,
            ScheduledMessageCount: 0,
            Status: "Active",
            CreatedAtUtc: new DateTimeOffset(2026, 6, 18, 8, 15, 0, TimeSpan.Zero),
            UpdatedAtUtc: ScenarioTime,
            LockDuration: kind == EntityKind.Topic ? null : TimeSpan.FromMinutes(1),
            MaxDeliveryCount: kind == EntityKind.Topic ? null : 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            RequiresSession: false,
            RequiresDuplicateDetection: false));
    }

    public static IReadOnlyList<ExplorerMessage> CreateMessages()
    {
        return
        [
            CreateMessage(
                1,
                "order-10482-placed",
                "OrderPlaced",
                new
                {
                    orderId = "ORD-10482",
                    customerId = "CUS-7321",
                    total = new { amount = 189.90, currency = "EUR" },
                    items = new[]
                    {
                        new { sku = "TRAIL-BAG-28L", quantity = 1 },
                        new { sku = "BOTTLE-STEEL-750", quantity = 2 }
                    },
                    occurredAtUtc = ScenarioTime.AddMinutes(-18)
                }),
            CreateMessage(
                2,
                "order-10482-paid",
                "PaymentAuthorized",
                new
                {
                    orderId = "ORD-10482",
                    paymentId = "PAY-880143",
                    amount = 189.90,
                    currency = "EUR",
                    provider = "ContosoPay",
                    occurredAtUtc = ScenarioTime.AddMinutes(-15)
                }),
            CreateMessage(
                3,
                "order-10482-reserved",
                "InventoryReserved",
                new
                {
                    orderId = "ORD-10482",
                    warehouse = "WAW-02",
                    reservationId = "RES-20451",
                    occurredAtUtc = ScenarioTime.AddMinutes(-11)
                }),
            CreateMessage(
                4,
                "order-10482-dispatched",
                "OrderDispatched",
                new
                {
                    orderId = "ORD-10482",
                    shipmentId = "SHP-55109",
                    carrier = "DHL",
                    trackingNumber = "PL839201485",
                    destination = "Krakow, PL",
                    occurredAtUtc = ScenarioTime.AddMinutes(-4)
                })
        ];
    }

    private static ExplorerMessage CreateMessage(
        long sequenceNumber,
        string messageId,
        string eventType,
        object data)
    {
        string body = JsonSerializer.Serialize(
            new { specVersion = "1.0", eventType, source = "retail-ordering", data },
            new JsonSerializerOptions { WriteIndented = true });
        DateTimeOffset enqueuedTime = ScenarioTime.AddMinutes(sequenceNumber - 5);

        return new ExplorerMessage(
            messageId,
            sequenceNumber,
            body,
            MessageBodyPreview.Create(body, 160),
            Encoding.UTF8.GetByteCount(body),
            enqueuedTime,
            enqueuedTime.AddDays(14),
            DeliveryCount: 1,
            ContentType: "application/json",
            CorrelationId: "ORD-10482",
            SessionId: null,
            Subject: eventType,
            new Dictionary<string, object?>
            {
                ["environment"] = "demo",
                ["eventType"] = eventType,
                ["region"] = "eu-central"
            },
            new Dictionary<string, object?>
            {
                ["SequenceNumber"] = sequenceNumber,
                ["EnqueuedTimeUtc"] = enqueuedTime
            });
    }
}
