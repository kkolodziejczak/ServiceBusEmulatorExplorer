using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>Creates inspection documents from observed messages without mutating their payloads.</summary>
public static class MessageInspectionPresenter
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true
    };

    static MessageInspectionPresenter()
    {
        IndentedJson.Converters.Add(new NonFiniteDoubleConverter());
        IndentedJson.Converters.Add(new NonFiniteSingleConverter());
    }

    public static InspectionText PresentBody(ExplorerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Present(message.Body);
    }

    public static InspectionText PresentProperties(ExplorerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        IReadOnlyDictionary<string, object?> projection = MessagePropertyProjection.Create(message);
        string rawText = JsonSerializer.Serialize(projection, IndentedJson);
        return Present(rawText);
    }

    public static InspectionText Present(string? rawText)
    {
        rawText ??= string.Empty;

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new InspectionText(
                rawText,
                rawText,
                JsonDocumentState.Empty,
                IsFormatted: false,
                "The body is empty.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawText);
            string formatted = JsonSerializer.Serialize(document.RootElement, IndentedJson);
            return new InspectionText(
                rawText,
                formatted,
                JsonDocumentState.Json,
                IsFormatted: true,
                "Formatted JSON.");
        }
        catch (JsonException)
        {
            JsonDocumentState state = LooksLikeJsonDocument(rawText)
                ? JsonDocumentState.MalformedJson
                : JsonDocumentState.NonJson;
            string description = state == JsonDocumentState.MalformedJson
                ? "Malformed JSON; showing the body as received."
                : "Plain text; showing the body as received.";
            return new InspectionText(rawText, rawText, state, IsFormatted: false, description);
        }
    }

    private static bool LooksLikeJsonDocument(string text)
    {
        char first = text.TrimStart()[0];
        return first is '{' or '[' or '"';
    }

    private sealed class NonFiniteDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Inspection metadata is display-only and is not deserialized.");
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsNaN(value))
            {
                writer.WriteStringValue("NaN");
            }
            else if (double.IsPositiveInfinity(value))
            {
                writer.WriteStringValue("Infinity");
            }
            else if (double.IsNegativeInfinity(value))
            {
                writer.WriteStringValue("-Infinity");
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }

    private sealed class NonFiniteSingleConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Inspection metadata is display-only and is not deserialized.");
        }

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (float.IsNaN(value))
            {
                writer.WriteStringValue("NaN");
            }
            else if (float.IsPositiveInfinity(value))
            {
                writer.WriteStringValue("Infinity");
            }
            else if (float.IsNegativeInfinity(value))
            {
                writer.WriteStringValue("-Infinity");
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }
}

/// <summary>Projects broker metadata while retaining system and application-property boundaries.</summary>
public static class MessagePropertyProjection
{
    public static IReadOnlyDictionary<string, object?> Create(ExplorerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["systemProperties"] = new Dictionary<string, object?>(message.SystemProperties, StringComparer.Ordinal),
            ["applicationProperties"] = new Dictionary<string, object?>(message.ApplicationProperties, StringComparer.Ordinal)
        };
    }
}
