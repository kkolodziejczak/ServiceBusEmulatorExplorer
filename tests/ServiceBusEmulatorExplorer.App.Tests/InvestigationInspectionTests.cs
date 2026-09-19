using System.Text.Json;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationInspectionTests
{
    [Fact]
    public void PresentBody_PreservesRawText_WhenJsonIsAlreadyFormattedDifferently()
    {
        const string raw = "{\"name\":\"Ada\", \"tags\":[1,2]}";
        ExplorerMessage message = CreateMessage(raw);

        InspectionText result = MessageInspectionPresenter.PresentBody(message);

        Assert.Equal(raw, result.RawText);
        Assert.Equal(raw, message.Body);
        Assert.Equal(JsonDocumentState.Json, result.State);
        Assert.True(result.IsFormatted);
        Assert.NotEqual(raw, result.DisplayText);
        Assert.Contains("\"name\": \"Ada\"", result.DisplayText);
    }

    [Fact]
    public void PresentBody_DistinguishesMalformedJsonFromPlainText()
    {
        InspectionText malformed = MessageInspectionPresenter.Present("{\"items\":[incomplete");
        InspectionText plainText = MessageInspectionPresenter.Present("Audit service started.");

        Assert.Equal(JsonDocumentState.MalformedJson, malformed.State);
        Assert.Equal(malformed.RawText, malformed.DisplayText);
        Assert.False(malformed.IsFormatted);
        Assert.Equal(JsonDocumentState.NonJson, plainText.State);
        Assert.Equal(plainText.RawText, plainText.DisplayText);
        Assert.False(plainText.IsFormatted);
    }

    [Fact]
    public void PresentBody_ReportsEmptyBodySeparately()
    {
        InspectionText result = MessageInspectionPresenter.Present("  \r\n");

        Assert.Equal(JsonDocumentState.Empty, result.State);
        Assert.Equal("  \r\n", result.RawText);
        Assert.Equal(result.RawText, result.DisplayText);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("\"Ada\"")]
    public void PresentBody_AcceptsTopLevelJsonValues(string raw)
    {
        InspectionText result = MessageInspectionPresenter.Present(raw);

        Assert.Equal(JsonDocumentState.Json, result.State);
        Assert.True(result.IsFormatted);
        Assert.Equal(raw, result.RawText);
    }

    [Fact]
    public void PresentProperties_ProjectsSystemAndApplicationPropertiesWithoutTruncatingLongValues()
    {
        string longValue = new('x', 32_000);
        ExplorerMessage message = CreateMessage("body") with
        {
            SystemProperties = new Dictionary<string, object?>
            {
                ["SequenceNumber"] = 17L,
                ["Diagnostic"] = longValue
            },
            ApplicationProperties = new Dictionary<string, object?>
            {
                ["trace"] = longValue
            }
        };

        InspectionText result = MessageInspectionPresenter.PresentProperties(message);

        Assert.Equal(JsonDocumentState.Json, result.State);
        Assert.Contains("\"systemProperties\"", result.DisplayText);
        Assert.Contains("\"applicationProperties\"", result.DisplayText);
        Assert.Contains(longValue, result.DisplayText);
        Assert.Equal(result.DisplayText, MessageInspectionPresenter.Present(result.DisplayText).DisplayText);
    }

    [Fact]
    public void PresentProperties_RendersLegalTypedMetadataWithoutChangingOriginalValues()
    {
        byte[] binary = [1, 2, 3];
        Guid guid = Guid.Parse("f7c6f2a7-3f96-4c97-8b2d-5e0cf1ec8e20");
        DateTimeOffset timestamp = new(2026, 9, 12, 10, 20, 30, TimeSpan.Zero);
        object?[] array = [binary, guid, null, timestamp];
        Dictionary<string, object?> applicationProperties = new(StringComparer.Ordinal)
        {
            ["binary"] = binary,
            ["guid"] = guid,
            ["timestamp"] = timestamp,
            ["missing"] = null,
            ["array"] = array,
            ["notANumber"] = double.NaN,
            ["positiveInfinity"] = float.PositiveInfinity,
            ["negativeInfinity"] = double.NegativeInfinity
        };
        ExplorerMessage message = CreateMessage("body") with { ApplicationProperties = applicationProperties };

        InspectionText result = MessageInspectionPresenter.PresentProperties(message);

        Assert.Equal(JsonDocumentState.Json, result.State);
        Assert.Contains("\"binary\": \"AQID\"", result.DisplayText);
        Assert.Contains(guid.ToString(), result.DisplayText);
        Assert.Contains("\"missing\": null", result.DisplayText);
        Assert.Contains("\"NaN\"", result.DisplayText);
        Assert.Contains("\"Infinity\"", result.DisplayText);
        Assert.Contains("\"-Infinity\"", result.DisplayText);
        using JsonDocument projected = JsonDocument.Parse(result.DisplayText);
        DateTimeOffset displayedTimestamp = projected.RootElement
            .GetProperty("applicationProperties")
            .GetProperty("timestamp")
            .GetDateTimeOffset();
        Assert.Equal(timestamp, displayedTimestamp);
        Assert.Same(binary, applicationProperties["binary"]);
        Assert.Same(array, applicationProperties["array"]);
        Assert.Equal(guid, applicationProperties["guid"]);
        Assert.Equal(timestamp, applicationProperties["timestamp"]);
    }

    [Fact]
    public void FindTokens_ClassifiesPropertyNamesAndValues()
    {
        IReadOnlyList<JsonTokenSpan> tokens = JsonSyntaxColorizer.FindTokens("{\"name\": \"Ada\", \"count\": 2, \"active\": true}");

        Assert.Equal(JsonTokenKind.PropertyName, tokens[0].Kind);
        Assert.Equal(JsonTokenKind.String, tokens[1].Kind);
        Assert.Equal(JsonTokenKind.PropertyName, tokens[2].Kind);
        Assert.Equal(JsonTokenKind.Scalar, tokens[3].Kind);
        Assert.Equal(JsonTokenKind.PropertyName, tokens[4].Kind);
        Assert.Equal(JsonTokenKind.Scalar, tokens[5].Kind);
    }

    [Fact]
    public void FindTokens_PreservesOffsetsForEscapedStringsAndNumbers()
    {
        const string text = "{\"say\":\"a\\\"b\",\"number\":-2.5e+3}";

        IReadOnlyList<JsonTokenSpan> tokens = JsonSyntaxColorizer.FindTokens(text);

        Assert.Equal("\"say\"", text.Substring(tokens[0].Start, tokens[0].Length));
        Assert.Equal("\"a\\\"b\"", text.Substring(tokens[1].Start, tokens[1].Length));
        Assert.Equal("\"number\"", text.Substring(tokens[2].Start, tokens[2].Length));
        Assert.Equal("-2.5e+3", text.Substring(tokens[3].Start, tokens[3].Length));
    }

    private static ExplorerMessage CreateMessage(string body)
    {
        return new ExplorerMessage(
            "message-id",
            1,
            body,
            body,
            body.Length,
            null,
            null,
            0,
            "application/json",
            "correlation-id",
            null,
            null,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>());
    }
}
