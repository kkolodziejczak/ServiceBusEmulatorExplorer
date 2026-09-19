using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Core.Tests.Investigation;

public sealed class MessageSearchQueryTests
{
    [Fact]
    public void Matches_uses_ordinal_case_sensitive_comparison_for_default_field()
    {
        MessageSearchQuery query = Parse("Message-42");

        Assert.True(query.Matches("Message-42", "correlation-42", defaultMessageId: true));
        Assert.False(query.Matches("message-42", "correlation-42", defaultMessageId: true));
        Assert.False(query.Matches("other-message", "Message-42", defaultMessageId: true));
    }

    [Fact]
    public void Matches_can_select_correlation_as_the_default_field()
    {
        MessageSearchQuery query = Parse("correlation-42");

        Assert.True(query.Matches("message-42", "correlation-42", defaultMessageId: false));
        Assert.False(query.Matches("correlation-42", "other-correlation", defaultMessageId: false));
    }

    [Fact]
    public void Or_is_case_insensitive_and_unions_all_alternatives()
    {
        MessageSearchQuery query = Parse("message-1 oR message-2");

        Assert.True(query.Matches("message-1", null, defaultMessageId: true));
        Assert.True(query.Matches("message-2", null, defaultMessageId: true));
        Assert.False(query.Matches("message-3", null, defaultMessageId: true));
    }

    [Fact]
    public void Explicit_fields_override_the_default_for_each_alternative()
    {
        MessageSearchQuery query = Parse("correlation:trace-1 OR message:message-2");

        Assert.True(query.Matches("message-2", "trace-1", defaultMessageId: true));
        Assert.True(query.Matches("message-2", "trace-1", defaultMessageId: false));
        Assert.True(query.Matches("message-1", "trace-1", defaultMessageId: true));
        Assert.False(query.Matches("message-1", "trace-2", defaultMessageId: true));
        Assert.True(query.CoversLiteral("trace-1", messageId: false, defaultMessageId: true));
        Assert.True(query.CoversLiteral("message-2", messageId: true, defaultMessageId: false));
        Assert.False(query.CoversLiteral("trace-1", messageId: true, defaultMessageId: true));
    }

    [Fact]
    public void Quoted_literals_preserve_spaces_or_operator_text_and_support_escapes()
    {
        MessageSearchQuery query = Parse("\"a OR b\\\"c\\\\d\"");

        Assert.True(query.Matches("a OR b\"c\\d", null, defaultMessageId: true));
        Assert.False(query.Matches("a", null, defaultMessageId: true));
        Assert.False(query.Matches("a OR b\\\"c\\\\d", null, defaultMessageId: true));
    }

    [Fact]
    public void Wildcards_match_any_characters_but_quoted_star_is_literal()
    {
        MessageSearchQuery wildcard = Parse("order-*-retry");
        MessageSearchQuery literal = Parse("\"order-*\"");

        Assert.True(wildcard.Matches("order-123-retry", null, defaultMessageId: true));
        Assert.True(wildcard.Matches("order--retry", null, defaultMessageId: true));
        Assert.False(wildcard.Matches("order-123-complete", null, defaultMessageId: true));
        Assert.True(literal.Matches("order-*", null, defaultMessageId: true));
        Assert.False(literal.Matches("order-123", null, defaultMessageId: true));
    }

    [Fact]
    public void Missing_correlation_ID_does_not_match_a_correlation_alternative()
    {
        MessageSearchQuery query = Parse("*");

        Assert.False(query.Matches("message-1", null, defaultMessageId: false));
        Assert.True(query.Matches("message-1", "correlation-1", defaultMessageId: false));
    }

    [Theory]
    [InlineData("message-1 OR", "Enter an ID on each side of OR.")]
    [InlineData("OR message-1", "Enter an ID on each side of OR.")]
    [InlineData("\"unclosed", "Close the quoted ID.")]
    [InlineData("message-\"1", "Close the quoted ID.")]
    [InlineData("message-\"1\"", "Quote the whole ID, or remove the quote.")]
    [InlineData("\"\"", "Enter an ID inside the quotes.")]
    [InlineData("", "Enter an ID on each side of OR.")]
    [InlineData("   ", "Enter an ID on each side of OR.")]
    public void TryParse_rejects_invalid_query_shapes(string text, string expectedError)
    {
        Assert.False(MessageSearchQuery.TryParse(text, out MessageSearchQuery? query, out string error));
        Assert.Null(query);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void QuoteLiteral_round_trips_IDs_with_quotes_and_backslashes()
    {
        const string id = "part\\one\"two";

        string quoted = MessageSearchQuery.QuoteLiteral(id);
        MessageSearchQuery query = Parse(quoted);

        Assert.True(query.Matches(id, null, defaultMessageId: true));
    }

    [Fact]
    public void Repeated_or_alternatives_match_each_observation_once()
    {
        MessageSearchQuery query = Parse("same OR same OR correlation:same");
        var observations = new[]
        {
            (MessageId: "same", CorrelationId: "different", Sequence: 1L),
            (MessageId: "same", CorrelationId: "different", Sequence: 2L),
            (MessageId: "other", CorrelationId: "same", Sequence: 3L)
        };

        var matches = observations
            .Where(observation => query.Matches(observation.MessageId, observation.CorrelationId, defaultMessageId: true))
            .Select(observation => observation.Sequence)
            .ToArray();

        Assert.Equal([1L, 2L, 3L], matches);
    }

    private static MessageSearchQuery Parse(string text)
    {
        Assert.True(MessageSearchQuery.TryParse(text, out MessageSearchQuery? query, out string error), error);
        return query!;
    }
}
