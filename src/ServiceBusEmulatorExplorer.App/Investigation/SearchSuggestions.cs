using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record SearchSuggestion(
    string Group,
    string Title,
    string Detail,
    string Kind,
    EntityNode? Entity = null,
    MessageRow? Message = null)
{
    public string Badge => Entity?.Kind ?? (Message is null ? "" : Message.IsDeadLetter ? "DLQ" : "Active");

    public string Icon => Entity?.Kind switch
    {
        nameof(EntityKind.Topic) => "M7,1 H13 V6 H7 Z M10,6 V10 M3,10 H17 M3,10 V14 M17,10 V14 M0,14 H6 V19 H0 Z M14,14 H20 V19 H14 Z",
        nameof(EntityKind.Subscription) => "M10,0 V12 M6,8 L10,12 14,8 M3,10 L0,19 H20 L17,10 M0,15 H6 L8,18 H12 L14,15 H20",
        nameof(EntityKind.Queue) => "M2,2 H18 V6 H2 Z M2,9 H18 V13 H2 Z M2,16 H18 V20 H2 Z",
        _ => Kind.StartsWith("search-", StringComparison.Ordinal)
            ? "M10,5 A5,5 0 1 1 0,5 A5,5 0 1 1 10,5 M9,9 L14,14"
            : "M2,0 H10 L15,5 V18 H2 Z M10,0 V5 H15 M5,9 H12 M5,13 H12"
    };
}

public sealed class SearchSuggestions
{
    private const int MaximumRecentRows = 1000;
    private readonly Dictionary<DeliveryIdentity, MessageRow> recentRows = [];
    private readonly LinkedList<DeliveryIdentity> recentOrder = [];

    public void Track(IEnumerable<MessageRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        foreach (MessageRow row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            DeliveryIdentity key = row.Key;
            if (recentRows.ContainsKey(key))
            {
                recentOrder.Remove(key);
            }

            recentRows[key] = row;
            recentOrder.AddLast(key);
        }

        while (recentOrder.Count > MaximumRecentRows)
        {
            DeliveryIdentity oldest = recentOrder.First!.Value;
            recentOrder.RemoveFirst();
            recentRows.Remove(oldest);
        }
    }

    public void Clear()
    {
        recentRows.Clear();
        recentOrder.Clear();
    }

    public IReadOnlyList<SearchSuggestion> Build(
        string text,
        IEnumerable<EntityNode> entities,
        bool connected)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(entities);

        string query = text.Trim();
        if (!connected || query.Length == 0)
        {
            return [];
        }

        List<SearchSuggestion> suggestions = [];
        suggestions.AddRange(entities
            .Where(entity => !entity.IsGroup && entity.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(entity => new SearchSuggestion("ENTITIES", entity.Path, "", "entity", entity)));

        suggestions.AddRange(recentOrder
            .Select(key => recentRows[key])
            .Where(row => !string.IsNullOrWhiteSpace(row.CorrelationId))
            .Select(row => row.CorrelationId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(id => new SearchSuggestion("CORRELATION IDS", id, "Search all entities", "correlation")));

        suggestions.AddRange(recentOrder
            .Select(key => recentRows[key])
            .Where(row => row.MessageId.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(row => new SearchSuggestion("MESSAGES · LOADED ONLY", row.MessageId, row.Source, "message", Message: row)));

        suggestions.Add(new SearchSuggestion(
            "SEARCH ALL ENTITIES",
            "Search all messages by correlation ID",
            query,
            "search-correlation"));
        suggestions.Add(new SearchSuggestion(
            "SEARCH ALL ENTITIES",
            "Search all messages by message ID",
            query,
            "search-message"));

        return suggestions;
    }
}
