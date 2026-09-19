using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationSuggestionTests
{
    [Fact]
    public void Build_groups_entity_correlation_message_and_global_actions_in_prototype_order()
    {
        var suggestions = new SearchSuggestions();
        EntityNode entity = Entity(EntityKind.Queue, "orders");
        MessageRow row = Row(1, "correlation-orders", "orders-1");
        suggestions.Track([row]);

        IReadOnlyList<SearchSuggestion> result = suggestions.Build("orders", [entity], connected: true);

        Assert.Equal(5, result.Count);
        Assert.Equal("ENTITIES", result[0].Group);
        Assert.Equal("orders", result[0].Title);
        Assert.Equal("CORRELATION IDS", result[1].Group);
        Assert.Equal("MESSAGES · LOADED ONLY", result[2].Group);
        Assert.Equal("SEARCH ALL ENTITIES", result[3].Group);
        Assert.Equal("search-correlation", result[3].Kind);
        Assert.Equal("search-message", result[4].Kind);
        Assert.Equal(nameof(EntityKind.Queue), result[0].Badge);
        Assert.NotEmpty(result[0].Icon);
        Assert.NotEmpty(result[3].Icon);
    }

    [Fact]
    public void Correlation_ids_match_case_insensitively_but_remain_case_sensitive_distinct()
    {
        var suggestions = new SearchSuggestions();
        suggestions.Track(
        [
            Row(1, "Alpha-1", "one"),
            Row(2, "ALPHA-1", "two"),
            Row(3, "alpha-2", "three"),
            Row(4, "Alpha-1", "four")
        ]);

        IReadOnlyList<SearchSuggestion> result = suggestions.Build("alpha", [], connected: true);

        Assert.Equal(new[] { "Alpha-1", "ALPHA-1", "alpha-2" },
            result.Where(item => item.Kind == "correlation").Select(item => item.Title));
    }

    [Fact]
    public void Tracking_updates_existing_identity_and_evicts_oldest_after_one_thousand_rows()
    {
        var suggestions = new SearchSuggestions();
        MessageRow first = Row(1, "first-correlation", "old-message");
        suggestions.Track([first]);
        suggestions.Track([Row(1, "updated-correlation", "new-message")]);

        IReadOnlyList<SearchSuggestion> updated = suggestions.Build("new-message", [], connected: true);
        suggestions.Track(Enumerable.Range(2, 1000).Select(sequence => Row(sequence, $"correlation-{sequence}", $"message-{sequence}")));
        IReadOnlyList<SearchSuggestion> evicted = suggestions.Build("old-message", [], connected: true);
        IReadOnlyList<SearchSuggestion> updatedEvicted = suggestions.Build("new-message", [], connected: true);

        Assert.Contains(updated, item => item.Kind == "message" && item.Title == "new-message");
        Assert.DoesNotContain(evicted, item => item.Kind == "message");
        Assert.DoesNotContain(updatedEvicted, item => item.Kind == "message");
    }

    [Fact]
    public void Empty_or_disconnected_build_returns_no_suggestions_and_clear_forgets_rows()
    {
        var suggestions = new SearchSuggestions();
        suggestions.Track([Row(1, "correlation", "message")]);

        Assert.Empty(suggestions.Build("message", [], connected: false));
        Assert.Empty(suggestions.Build("", [], connected: true));
        suggestions.Clear();
        Assert.DoesNotContain(suggestions.Build("message", [], connected: true), item => item.Kind == "message");
    }

    private static MessageRow Row(long sequence, string correlationId, string messageId)
    {
        var address = new EntityAddress(EntityKind.Queue, "orders");
        var delivery = new MessageDelivery(
            new DeliveryIdentity(1, address, MessageBucket.Active, sequence),
            new ExplorerMessage(
                messageId,
                sequence,
                "body",
                "body",
                4,
                null,
                null,
                0,
                null,
                correlationId,
                null,
                null,
                new Dictionary<string, object?>(),
                new Dictionary<string, object?>()));
        return new MessageRow(delivery, TimestampDisplay.Utc);
    }

    private static EntityNode Entity(EntityKind kind, string name)
    {
        var node = new ServiceBusEntityNode(
            kind,
            name,
            kind == EntityKind.Subscription ? "orders" : null,
            new EntityRuntimeCounts(0, 0, 0, 0),
            new EntityMetadata(name, "Active", null, null, null, null, null, null, null));
        return new EntityNode(name, kind.ToString(), new EntityObservation(node,
            new EntityCountObservation(
                new(0, CountAvailability.Known),
                new(0, CountAvailability.Known),
                new(0, CountAvailability.Known))));
    }
}
