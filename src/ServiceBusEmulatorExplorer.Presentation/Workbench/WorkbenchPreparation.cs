using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>One valid row of sample data to prepare a message from.</summary>
public sealed record PreparationRow(int Row, string CustomerId, decimal Amount);

/// <summary>A template variable as declared in Compose: where its value comes from and its default, if it has one.</summary>
public sealed record TemplateVariable(string Name, string Type, string Source, bool HasDefault, string DefaultValue);

/// <summary>
/// What a template gives preparation: its body, its declared variables, the other texts that may reference variables
/// (application property values and message properties), and the custom message ID when the user set one.
/// </summary>
public sealed record PreparationTemplate(string Body, IReadOnlyList<TemplateVariable> Variables, IReadOnlyList<string> PropertyTexts,
    string? CustomMessageId);

/// <summary>A message ready for review, with the variable values its body was expanded with.</summary>
public sealed record WorkbenchPreparedMessage(int Row, string MessageId, string EventId, string OccurredAt, string Body,
    IReadOnlyDictionary<string, string> VariableValues);

/// <summary>
/// The prepared messages, or <c>null</c> when none could be prepared: then a variable without a value has an
/// <see cref="Error"/>, and a body that is not JSON has none.
/// </summary>
public sealed record PreparationResult(IReadOnlyList<WorkbenchPreparedMessage>? Messages, string? Error);

/// <summary>
/// Prepares Message Workbench messages: each row gets a new event ID, the template's $(Name) variables are replaced
/// with the row's values, defaults and generated values, and the body is checked to be JSON and indented.
/// </summary>
public static class WorkbenchPreparation
{
    private static readonly Regex VariableToken = new(
        "(?<quoted>\\\")?\\$\\((?<name>[A-Za-z_][A-Za-z0-9_]*)\\)(?(quoted)\\\")",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <param name="now">When the messages occur; they record it in UTC.</param>
    /// <param name="newEventId">Creates each message's event ID.</param>
    public static PreparationResult Prepare(IEnumerable<PreparationRow> rows, PreparationTemplate template, DateTimeOffset now,
        Func<Guid> newEventId)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(newEventId);
        string occurredAt = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var prepared = new List<WorkbenchPreparedMessage>();
        try
        {
            foreach (var row in rows)
            {
                string eventId = newEventId().ToString("D");
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CustomerId"] = row.CustomerId,
                    ["Amount"] = row.Amount.ToString(CultureInfo.InvariantCulture),
                    ["EventId"] = eventId,
                    ["OccurredAt"] = occurredAt
                };
                foreach (var variable in template.Variables)
                {
                    if (values.ContainsKey(variable.Name)) continue;
                    if (variable.Source == "Input" && variable.HasDefault)
                    {
                        values[variable.Name] = variable.DefaultValue;
                        continue;
                    }
                    if (IsReferenced(template, variable.Name))
                        throw new PreparationException($"Variable '{variable.Name}' requires a default value before this message can be reviewed.");
                }

                using var json = JsonDocument.Parse(Expand(template, values));
                string body = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
                prepared.Add(new(row.Row, template.CustomMessageId ?? eventId, eventId, occurredAt, body, values));
            }
        }
        catch (PreparationException exception)
        {
            return new(null, exception.Message);
        }
        catch (JsonException)
        {
            return new(null, null);
        }
        return new(prepared.ToArray(), null);
    }

    private static bool IsReferenced(PreparationTemplate template, string name)
    {
        bool Contains(string value) => VariableToken.Matches(value)
            .Any(match => string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase));

        return Contains(template.Body) || template.PropertyTexts.Any(Contains);
    }

    private static string Expand(PreparationTemplate template, IReadOnlyDictionary<string, string> values) =>
        VariableToken.Replace(template.Body, match =>
        {
            string name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out string? value))
                throw new PreparationException($"Variable '{name}' is not declared.");
            if (string.Equals(name, "Amount", StringComparison.OrdinalIgnoreCase) ||
                template.Variables.FirstOrDefault(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Type == "number")
                return value;

            string encoded = JsonSerializer.Serialize(value);
            return match.Groups["quoted"].Success ? encoded : encoded[1..^1];
        });

    private sealed class PreparationException(string message) : Exception(message);
}
