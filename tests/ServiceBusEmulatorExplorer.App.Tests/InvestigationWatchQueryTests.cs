using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationWatchQueryTests
{
    [Fact]
    public void Mixed_arrivals_prefer_correlation_and_qualify_message_only_cases()
    {
        var request = WatchInvestigationQuery.Build(
            [Delivery("m-1", "case-1"), Delivery("m-2", null), Delivery("m-3", "case-1")],
            "", true, false);

        Assert.False(request.DefaultMessageId);
        Assert.Equal("\"case-1\" OR message:\"m-2\"", request.Query);
        var query = Parse(request);
        Assert.True(query.Matches("other", "case-1", request.DefaultMessageId));
        Assert.True(query.Matches("m-2", null, request.DefaultMessageId));
        Assert.False(query.Matches("case-1", "m-2", request.DefaultMessageId));
    }

    [Fact]
    public void All_missing_or_whitespace_correlations_select_message_default()
    {
        var request = WatchInvestigationQuery.Build(
            [Delivery("first", null), Delivery("second", " \t ")], "ignored", false, false);

        Assert.True(request.DefaultMessageId);
        Assert.Equal("\"first\" OR \"second\"", request.Query);
    }

    [Theory]
    [InlineData("literal OR alternative")]
    [InlineData("case*literal")]
    [InlineData("quote\"and\\slash")]
    [InlineData("correlation:field-looking")]
    public void Generated_terms_round_trip_as_exact_literals(string id)
    {
        var request = WatchInvestigationQuery.Build([Delivery("unrelated", id)], "", false, false);
        var query = Parse(request);

        Assert.Equal(MessageSearchQuery.QuoteLiteral(id), request.Query);
        Assert.True(query.Matches("unrelated", id, request.DefaultMessageId));
        Assert.False(query.Matches("unrelated", id + "-suffix", request.DefaultMessageId));
        Assert.False(query.Matches("unrelated", id.Replace("*", "expanded") + "-other", request.DefaultMessageId));
    }

    [Fact]
    public void Preserves_valid_existing_query_and_its_default_while_adding_correlation_cases()
    {
        const string existing = "message:old-* OR \"existing\"";
        var request = WatchInvestigationQuery.Build(
            [Delivery("new-message", "new-case")], existing, true, true);

        Assert.True(request.DefaultMessageId);
        Assert.Equal(existing + " OR correlation:\"new-case\"", request.Query);
        var query = Parse(request);
        Assert.True(query.Matches("old-7", null, request.DefaultMessageId));
        Assert.True(query.Matches("existing", null, request.DefaultMessageId));
        Assert.True(query.Matches("anything", "new-case", request.DefaultMessageId));
    }

    [Fact]
    public void Existing_wildcard_covers_only_its_requested_field()
    {
        var request = WatchInvestigationQuery.Build(
            [Delivery("not-used", "case-1"), Delivery("case-1", null)], "case-*", false, true);

        Assert.Equal("case-* OR message:\"case-1\"", request.Query);
        Assert.False(request.DefaultMessageId);
    }

    [Fact]
    public void Quoted_star_does_not_cover_a_different_literal()
    {
        var request = WatchInvestigationQuery.Build(
            [Delivery("m", "case-1"), Delivery("m2", "case-1")], "\"case-*\"", false, true);

        Assert.Equal("\"case-*\" OR \"case-1\"", request.Query);
    }

    [Theory]
    [InlineData("\"unfinished")]
    [InlineData("case OR")]
    [InlineData("")]
    public void Invalid_existing_query_is_replaced_and_default_recomputed(string existing)
    {
        var request = WatchInvestigationQuery.Build([Delivery("m", "case")], existing, true, true);

        Assert.False(request.DefaultMessageId);
        Assert.Equal("\"case\"", request.Query);
    }

    [Fact]
    public void Empty_arrivals_cannot_start_an_investigation()
    {
        Assert.Throws<ArgumentException>(() => WatchInvestigationQuery.Build([], "valid", true, true));
    }

    private static MessageSearchQuery Parse(WatchSearchRequest request)
    {
        Assert.True(MessageSearchQuery.TryParse(request.Query, out var query, out var error), error);
        return query!;
    }

    private static MessageDelivery Delivery(string messageId, string? correlationId)
    {
        var message = new ExplorerMessage(messageId, 1, "{}", "{}", 2,
            null, null, 0, null, correlationId, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new MessageDelivery(new DeliveryIdentity(7,
            new EntityAddress(EntityKind.Queue, "orders"), MessageBucket.DeadLetter, 1), message);
    }
}
