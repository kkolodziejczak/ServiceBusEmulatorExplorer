using System.Text;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

// ID matching is ordinal. OR joins alternatives; quoted alternatives are literal IDs.
public sealed class MessageSearchQuery
{
    private readonly IReadOnlyList<Alternative> alternatives;

    private MessageSearchQuery(IReadOnlyList<Alternative> alternatives) => this.alternatives = alternatives;

    public static bool TryParse(string text, out MessageSearchQuery? query, out string error)
    {
        query = null;
        error = "";
        var alternatives = new List<Alternative>();
        var start = 0;
        var quoted = false;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && quoted && text[index] == '\\')
            {
                // Inside quotes, only quote and backslash are escaped. Other backslashes are literal.
                if (index + 1 < text.Length && text[index + 1] is '"' or '\\') index++;
                continue;
            }
            if (index < text.Length && text[index] == '"') { quoted = !quoted; continue; }
            if (quoted || (index < text.Length && !IsOr(text, index))) continue;
            if (!TryAlternative(text[start..index].Trim(), out var alternative, out error)) return false;
            alternatives.Add(alternative!);
            if (index < text.Length) index++;
            start = index + 1;
        }
        if (quoted) { error = "Close the quoted ID."; return false; }
        query = new(alternatives);
        return true;
    }

    private static bool IsOr(string text, int index) => index + 1 < text.Length
        && (text[index] is 'O' or 'o') && (text[index + 1] is 'R' or 'r')
        && (index == 0 || char.IsWhiteSpace(text[index - 1]))
        && (index + 2 == text.Length || char.IsWhiteSpace(text[index + 2]));

    private static bool TryAlternative(string text, out Alternative? alternative, out string error)
    {
        alternative = null;
        error = "";
        if (text.Length == 0) { error = "Enter an ID on each side of OR."; return false; }
        if (text[0] != '"')
        {
            if (text.Contains('"')) { error = "Quote the whole ID, or remove the quote."; return false; }
            alternative = new(text, true);
            return true;
        }
        var literal = new StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (index != text.Length - 1) { error = "Separate quoted IDs with OR."; return false; }
                if (literal.Length == 0) { error = "Enter an ID inside the quotes."; return false; }
                alternative = new(literal.ToString(), false);
                return true;
            }
            if (character == '\\' && index + 1 < text.Length && text[index + 1] is '"' or '\\')
                character = text[++index];
            literal.Append(character);
        }
        error = "Close the quoted ID.";
        return false;
    }

    public static string QuoteLiteral(string id) => "\"" + id.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public bool Matches(string id) => alternatives.Any(alternative => alternative.Wildcards
        ? MatchesWildcard(alternative.Text, id) : string.Equals(alternative.Text, id, StringComparison.Ordinal));

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
            else return false;
        }
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;
        return patternIndex == pattern.Length;
    }

    private sealed record Alternative(string Text, bool Wildcards);
}
