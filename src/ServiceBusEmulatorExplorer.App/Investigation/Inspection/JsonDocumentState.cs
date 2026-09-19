namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>Describes how an inspection document was interpreted.</summary>
public enum JsonDocumentState
{
    Json,
    MalformedJson,
    NonJson,
    Empty
}

/// <summary>One syntax token that can be rendered without changing the source text.</summary>
public sealed record JsonTokenSpan(int Start, int Length, JsonTokenKind Kind);

public enum JsonTokenKind
{
    PropertyName,
    String,
    Scalar
}

/// <summary>The raw document and the safe display form used by an inspector.</summary>
public sealed record InspectionText(
    string RawText,
    string DisplayText,
    JsonDocumentState State,
    bool IsFormatted,
    string StateDescription);
