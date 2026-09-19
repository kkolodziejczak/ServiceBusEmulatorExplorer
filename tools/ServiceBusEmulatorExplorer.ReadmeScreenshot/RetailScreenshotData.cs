using System.Text;
using System.Text.Json;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.Messaging;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class RetailScreenshotData
{
    public static readonly DateTimeOffset ScenarioTime = new(2026, 7, 21, 19, 36, 29, TimeSpan.Zero);

    public static WorkspacePreferences CreatePreferences() => new()
    {
        Profiles =
        [
            new InvestigationProfile(
                "demo-retail",
                new ConnectionProfile("Demo retail workspace", "synthetic-runtime", "synthetic-admin"),
                "#0069FA")
        ],
        SelectedProfileId = "demo-retail",
        WasConnected = false,
        LogExpanded = true,
        TimestampDisplay = TimestampDisplay.Utc,
        WindowWidth = 1500,
        WindowHeight = 1000,
        QueuePageSize = 50,
        TopicPageSize = 50,
        SubscriptionPageSize = 50
    };

    public static EntityDiscoverySnapshot CreateSnapshot()
    {
        ServiceBusEntityNode[] entities =
        [
            CreateEntity(EntityKind.Queue, "checkout-commands", null, active: 18, deadLetter: 1, scheduled: 0),
            CreateEntity(EntityKind.Topic, "order-events", null, active: 16, deadLetter: 1, scheduled: 3),
            CreateEntity(EntityKind.Subscription, "fulfillment", "order-events", active: 8, deadLetter: 1, scheduled: 0),
            CreateEntity(EntityKind.Subscription, "customer-notifications", "order-events", active: 8, deadLetter: 0, scheduled: 0),
            CreateEntity(EntityKind.Topic, "inventory-events", null, active: 6, deadLetter: 0, scheduled: 0),
            CreateEntity(EntityKind.Subscription, "warehouse", "inventory-events", active: 6, deadLetter: 0, scheduled: 0)
        ];

        return new(
            entities.Select(entity => new EntityObservation(
                entity,
                new EntityCountObservation(
                    new(entity.Counts.ActiveMessageCount, CountAvailability.Known),
                    new(entity.Counts.DeadLetterMessageCount, CountAvailability.Known),
                    new(entity.Counts.ScheduledMessageCount, CountAvailability.Known)))).ToArray(),
            ScenarioTime,
            IsComplete: true,
            Issues: []);
    }

    private static ServiceBusEntityNode CreateEntity(
        EntityKind kind,
        string name,
        string? topicName,
        long active,
        long deadLetter,
        long scheduled)
    {
        return EntityTreeBuilder.CreateNode(new EntityTreeSource(
            kind,
            name,
            topicName,
            active,
            deadLetter,
            scheduled,
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
        var messages = new List<ExplorerMessage>();
        string[] subscriptions = ["fulfillment", "customer-notifications"];
        for (int index = 0; index < 8; index++)
        {
            foreach (string subscription in subscriptions)
            {
                string eventType = index switch
                {
                    0 => "OrderPlaced",
                    1 => "PaymentAuthorized",
                    2 => "InventoryReserved",
                    3 => "OrderDispatched",
                    _ => "OrderStatusUpdated"
                };
                string messageId = index == 3 && subscription == "fulfillment"
                    ? "order-10482-dispatched"
                    : $"order-10482-{eventType.ToLowerInvariant()}-{subscription}";
                messages.Add(CreateMessage(
                    subscription,
                    index + 1,
                    messageId,
                    eventType,
                    new
                    {
                        orderId = "ORD-10482",
                        customerId = "CUS-7321",
                        shipmentId = eventType == "OrderDispatched" ? "SHP-55109" : null,
                        destination = eventType == "OrderDispatched" ? "Krakow, PL" : null,
                        total = new { amount = 189.90, currency = "EUR" },
                        occurredAtUtc = ScenarioTime.AddMinutes(-18 + index)
                    }));
            }
        }

        return messages;
    }

    private static ExplorerMessage CreateMessage(
        string subscription,
        int sequenceNumber,
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
                ["region"] = "eu-central",
                ["subscription"] = subscription
            },
            new Dictionary<string, object?>
            {
                ["SequenceNumber"] = sequenceNumber,
                ["EnqueuedTimeUtc"] = enqueuedTime
            })
        {
            RawBody = BinaryData.FromString(body)
        };
    }
}
