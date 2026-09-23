using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private static readonly Regex VariableToken = new(
        "(?<quoted>\\\")?\\$\\((?<name>[A-Za-z_][A-Za-z0-9_]*)\\)(?(quoted)\\\")",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private IReadOnlyList<PrototypePreparedMessage> preparedMessages = [];
    private string? preparationError;

    private void ClearPreparedMessages()
    {
        preparedMessages = [];
        preparationError = null;
        if (SingleEventId is not null) SingleEventId.Text = "Waiting for valid inputs";
        if (SingleOccurredAt is not null) SingleOccurredAt.Text = "Waiting for valid inputs";
    }

    private bool PrepareMessages(IEnumerable<CsvPreviewRow> rows)
    {
        string occurredAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var prepared = new List<PrototypePreparedMessage>();
        try
        {
            foreach (var row in rows)
            {
                string eventId = Guid.NewGuid().ToString("D");
                string messageId = CustomMessageId.IsChecked == true ? CustomMessageIdInput.Text : eventId;
                string amount = row.Amount!.Value.ToString(CultureInfo.InvariantCulture);
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CustomerId"] = row.CustomerId,
                    ["Amount"] = amount,
                    ["EventId"] = eventId,
                    ["OccurredAt"] = occurredAt
                };
                foreach (var variable in variables)
                {
                    if (values.ContainsKey(variable.Name)) continue;
                    if (variable.Source == "Input" && variable.HasDefault)
                    {
                        values[variable.Name] = variable.DefaultValue;
                        continue;
                    }
                    if (IsVariableReferenced(variable.Name))
                        throw new PrototypePreparationException(
                            $"Variable '{variable.Name}' requires a default value before this message can be reviewed.");
                }

                string body = ExpandTemplate(currentBody, values, textMode);
                if (!textMode)
                {
                    using var json = JsonDocument.Parse(body);
                    body = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                prepared.Add(new PrototypePreparedMessage(row.Row, messageId, eventId, occurredAt, body)
                {
                    VariableValues = values
                });
            }
        }
        catch (PrototypePreparationException exception)
        {
            preparationError = exception.Message;
            PreviewText.Text = "No preview generated.";
            return false;
        }
        catch (JsonException)
        {
            preparationError = null;
            PreviewText.Text = "No preview generated.";
            return false;
        }

        preparedMessages = prepared.ToArray();
        if (SingleMode.IsChecked == true && prepared.Count == 1)
        {
            SingleEventId.Text = prepared[0].EventId;
            SingleOccurredAt.Text = prepared[0].OccurredAt;
        }
        return true;
    }

    private bool IsVariableReferenced(string name)
    {
        bool Contains(string value) => VariableToken.Matches(value)
            .Cast<Match>()
            .Any(match => string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase));

        return Contains(currentBody) ||
            applicationProperties.Any(property => Contains(property.Value)) ||
            Contains(PropertySubject.Text) || Contains(PropertyCorrelationId.Text) ||
            Contains(PropertySessionId.Text) || Contains(PropertyReplyTo.Text) ||
            Contains(PropertyReplySessionId.Text) || Contains(PropertyPartitionKey.Text);
    }

    private string ExpandTemplate(string template, IReadOnlyDictionary<string, string> values, bool plainText)
    {
        return VariableToken.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out string? value))
                throw new PrototypePreparationException($"Variable '{name}' is not declared.");
            if (plainText) return value;
            if (string.Equals(name, "Amount", StringComparison.OrdinalIgnoreCase) ||
                variables.FirstOrDefault(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Type == "number")
                return value;

            string encoded = JsonSerializer.Serialize(value);
            return match.Groups["quoted"].Success ? encoded : encoded[1..^1];
        });
    }

    private sealed class PrototypePreparationException(string message) : Exception(message);
}
