using System.Text;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>A template's message properties as entered in Compose; application properties are the non-empty rows.</summary>
public sealed record MessagePropertyTexts(string Subject, string ContentType, string CorrelationId, string SessionId, string ReplyTo,
    string ReplySessionId, string PartitionKey, IReadOnlyList<(string Name, string Type, string Value)> ApplicationProperties);

/// <summary>The message properties as the Workbench shows them in the preview and the review, with variables filled in.</summary>
public static class MessagePropertyDetails
{
    public static string Describe(MessagePropertyTexts properties, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(values);
        string Resolve(string value) => Expand(value, values);
        return string.Join("\n",
            $"Subject: {Resolve(properties.Subject)}",
            $"Content type: {properties.ContentType}",
            $"Correlation ID: {Resolve(properties.CorrelationId)}",
            $"Session ID: {Resolve(properties.SessionId)}",
            $"Reply to: {Resolve(properties.ReplyTo)}",
            $"Reply session ID: {Resolve(properties.ReplySessionId)}",
            $"Partition key: {(string.IsNullOrWhiteSpace(properties.PartitionKey) ? "none" : "unsupported by emulator")}",
            "Application properties: " + string.Join(", ", properties.ApplicationProperties.Select(property =>
                $"{property.Name} ({property.Type}) = {Resolve(property.Value)}")));
    }

    /// <summary>
    /// Replaces each $(Name) in <paramref name="value"/> with its value when <paramref name="values"/> has one, and
    /// leaves unknown or unclosed references as they are.
    /// </summary>
    public static string Expand(string value, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(values);
        int cursor = 0;
        var result = new StringBuilder(value.Length);
        while (cursor < value.Length)
        {
            int start = value.IndexOf("$(", cursor, StringComparison.Ordinal);
            if (start < 0) { result.Append(value, cursor, value.Length - cursor); break; }
            result.Append(value, cursor, start - cursor);
            int end = value.IndexOf(')', start + 2);
            if (end < 0) { result.Append(value, start, value.Length - start); break; }
            string name = value[(start + 2)..end];
            result.Append(values.TryGetValue(name, out string? resolved) ? resolved : value[start..(end + 1)]);
            cursor = end + 1;
        }
        return result.ToString();
    }
}
