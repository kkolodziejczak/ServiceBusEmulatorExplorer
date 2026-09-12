using System.Text;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>
/// A pure matcher for the investigation workspace's message and correlation ID grammar.
/// </summary>
/// <remarks>
/// ID values are matched with ordinal, case-sensitive comparison. Alternatives are joined
/// with a standalone, case-insensitive <c>OR</c> operator. A caller supplies the default
/// field for unqualified alternatives; an individual alternative may override it with
/// <c>message:</c> or <c>correlation:</c>.
/// </remarks>
public sealed class MessageSearchQuery
{
    private readonly IReadOnlyList<Alternative> alternatives;

    private MessageSearchQuery(IReadOnlyList<Alternative> alternatives)
    {
        this.alternatives = alternatives;
    }

    /// <summary>Parses a query without inspecting or contacting a broker.</summary>
    public static bool TryParse(string text, out MessageSearchQuery? query, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        query = null;
        error = string.Empty;
        var parsedAlternatives = new List<Alternative>();
        var alternativeStart = 0;
        var quoted = false;

        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && quoted && text[index] == '\\')
            {
                // Within a quoted literal only quote and backslash are escapes. Keep all
                // other backslashes as part of the literal.
                if (index + 1 < text.Length && text[index + 1] is '"' or '\\')
                {
                    index++;
                }

                continue;
            }

            if (index < text.Length && text[index] == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted || (index < text.Length && !IsOr(text, index)))
            {
                continue;
            }

            if (!TryParseAlternative(text[alternativeStart..index].Trim(), out var alternative, out error))
            {
                return false;
            }

            parsedAlternatives.Add(alternative!);
            if (index < text.Length)
            {
                index++;
            }

            alternativeStart = index + 1;
        }

        if (quoted)
        {
            error = "Close the quoted ID.";
            return false;
        }

        if (parsedAlternatives.Count == 0)
        {
            error = "Enter an ID on each side of OR.";
            return false;
        }

        query = new MessageSearchQuery(parsedAlternatives);
        return true;
    }

    /// <summary>Quotes an ID so it can be used as one literal query alternative.</summary>
    public static string QuoteLiteral(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return "\"" + id.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Returns whether any alternative matches the selected ID field.
    /// </summary>
    /// <param name="messageId">The message ID of the observed message.</param>
    /// <param name="correlationId">The correlation ID, if the message has one.</param>
    /// <param name="defaultMessageId">
    /// Selects message ID for unqualified alternatives when true; otherwise correlation ID.
    /// </param>
    public bool Matches(string messageId, string? correlationId, bool defaultMessageId)
    {
        ArgumentNullException.ThrowIfNull(messageId);

        return alternatives.Any(alternative =>
        {
            var useMessageId = alternative.MessageId ?? defaultMessageId;
            var value = useMessageId ? messageId : correlationId;
            return value is not null && MatchesAlternative(alternative, value);
        });
    }

    /// <summary>
    /// Returns whether an ID is covered by an alternative targeting the requested field.
    /// This supports callers that need to classify or union results by field.
    /// </summary>
    public bool CoversLiteral(string id, bool messageId, bool defaultMessageId)
    {
        ArgumentNullException.ThrowIfNull(id);

        return alternatives.Any(alternative =>
            (alternative.MessageId ?? defaultMessageId) == messageId
            && MatchesAlternative(alternative, id));
    }

    private static bool IsOr(string text, int index)
    {
        return index + 1 < text.Length
            && text[index] is 'O' or 'o'
            && text[index + 1] is 'R' or 'r'
            && (index == 0 || char.IsWhiteSpace(text[index - 1]))
            && (index + 2 == text.Length || char.IsWhiteSpace(text[index + 2]));
    }

    private static bool TryParseAlternative(
        string text,
        out Alternative? alternative,
        out string error)
    {
        alternative = null;
        error = string.Empty;
        bool? messageId = null;

        if (text.StartsWith("correlation:", StringComparison.OrdinalIgnoreCase))
        {
            messageId = false;
            text = text["correlation:".Length..].Trim();
        }
        else if (text.StartsWith("message:", StringComparison.OrdinalIgnoreCase))
        {
            messageId = true;
            text = text["message:".Length..].Trim();
        }

        if (text.Length == 0)
        {
            error = "Enter an ID on each side of OR.";
            return false;
        }

        if (text[0] != '"')
        {
            if (text.Contains('"'))
            {
                error = "Quote the whole ID, or remove the quote.";
                return false;
            }

            alternative = new Alternative(text, Wildcards: text.Contains('*'), messageId);
            return true;
        }

        var literal = new StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (index != text.Length - 1)
                {
                    error = "Separate quoted IDs with OR.";
                    return false;
                }

                if (literal.Length == 0)
                {
                    error = "Enter an ID inside the quotes.";
                    return false;
                }

                alternative = new Alternative(literal.ToString(), Wildcards: false, messageId);
                return true;
            }

            if (character == '\\' && index + 1 < text.Length && text[index + 1] is '"' or '\\')
            {
                character = text[++index];
            }

            literal.Append(character);
        }

        error = "Close the quoted ID.";
        return false;
    }

    private static bool MatchesAlternative(Alternative alternative, string id)
    {
        return alternative.Wildcards
            ? MatchesWildcard(alternative.Text, id)
            : string.Equals(alternative.Text, id, StringComparison.Ordinal);
    }

    private static bool MatchesWildcard(string pattern, string id)
    {
        var patternIndex = 0;
        var idIndex = 0;
        var starIndex = -1;
        var retryIndex = 0;

        while (idIndex < id.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryIndex = idIndex;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == id[idIndex])
            {
                patternIndex++;
                idIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                idIndex = ++retryIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private sealed record Alternative(string Text, bool Wildcards, bool? MessageId);
}
