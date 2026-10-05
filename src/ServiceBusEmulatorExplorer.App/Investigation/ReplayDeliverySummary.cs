using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public static class ReplayDeliverySummary
{
    public static string Format(IEnumerable<MessageRow> messages, bool complete)
    {
        var rows = messages.ToArray();
        int ids = rows.Select(row => row.MessageId).Distinct(StringComparer.Ordinal).Count();
        var sources = rows.Select(row => row.Key.Source).Distinct().ToArray();
        string entity = sources.Length > 0 && sources.All(source => source.Kind == EntityKind.Subscription)
            ? sources.Length == 1 ? "subscription" : "subscriptions"
            : sources.Length > 0 && sources.All(source => source.Kind == EntityKind.Queue)
                ? sources.Length == 1 ? "queue" : "queues"
                : sources.Length == 1 ? "receiving entity" : "receiving entities";
        return $"{ids:N0} replay {(ids == 1 ? "ID" : "IDs")} · {rows.Length:N0} {(rows.Length == 1 ? "delivery" : "deliveries")} across {sources.Length:N0} {entity}"
            + (complete ? string.Empty : " · found so far");
    }
}
