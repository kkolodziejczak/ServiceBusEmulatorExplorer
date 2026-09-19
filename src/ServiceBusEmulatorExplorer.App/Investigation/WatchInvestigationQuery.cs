using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record WatchSearchRequest(string Query, bool DefaultMessageId);

public static class WatchInvestigationQuery
{
    public static WatchSearchRequest Build(IReadOnlyList<MessageDelivery> messages, string existingQuery,
        bool existingDefaultMessageId, bool preserveExisting)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0) throw new ArgumentException("Select at least one watched delivery.", nameof(messages));
        bool keep = preserveExisting && MessageSearchQuery.TryParse(existingQuery, out _, out _);
        bool byMessageId = keep ? existingDefaultMessageId : messages.All(message => string.IsNullOrWhiteSpace(message.Message.CorrelationId));
        string text = keep ? existingQuery : "";
        MessageSearchQuery.TryParse(text, out var query, out _);
        foreach (var delivery in messages)
        {
            bool useMessageId = string.IsNullOrWhiteSpace(delivery.Message.CorrelationId);
            string id = useMessageId ? delivery.Message.MessageId : delivery.Message.CorrelationId!;
            if (query?.CoversLiteral(id, useMessageId, byMessageId) == true) continue;
            string prefix = useMessageId == byMessageId ? "" : useMessageId ? "message:" : "correlation:";
            string term = prefix + MessageSearchQuery.QuoteLiteral(id);
            text = text.Length == 0 ? term : text + " OR " + term;
            MessageSearchQuery.TryParse(text, out query, out _);
        }
        return new(text, byMessageId);
    }
}
