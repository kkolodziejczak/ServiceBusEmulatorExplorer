using System.Globalization;
using System.Text.Json;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public partial class MessageLibraryPrototypeView
{
    private IReadOnlyList<PrototypePreparedMessage> preparedMessages = [];

    private void ClearPreparedMessages()
    {
        preparedMessages = [];
        if (SingleEventId is not null) SingleEventId.Text = "Generated when validated";
        if (SingleOccurredAt is not null) SingleOccurredAt.Text = "Generated when validated";
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
                string customer = textMode ? row.CustomerId : JsonSerializer.Serialize(row.CustomerId)[1..^1];
                string body = currentBody
                    .Replace("$(CustomerId)", customer, StringComparison.Ordinal)
                    .Replace("\"$(Amount)\"", amount, StringComparison.Ordinal)
                    .Replace("$(Amount)", amount, StringComparison.Ordinal)
                    .Replace("$(EventId)", eventId, StringComparison.Ordinal)
                    .Replace("$(OccurredAt)", occurredAt, StringComparison.Ordinal);
                if (!textMode)
                {
                    using var json = JsonDocument.Parse(body);
                    body = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                prepared.Add(new PrototypePreparedMessage(row.Row, messageId, eventId, occurredAt, body));
            }
        }
        catch (JsonException)
        {
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
}
