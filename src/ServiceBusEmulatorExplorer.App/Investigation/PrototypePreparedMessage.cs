namespace ServiceBusEmulatorExplorer.App.Investigation;

public record PrototypePreparedMessage(
    int Row,
    string MessageId,
    string EventId,
    string OccurredAt,
    string Body)
{
    public IReadOnlyDictionary<string, string>? VariableValues { get; init; }
}
