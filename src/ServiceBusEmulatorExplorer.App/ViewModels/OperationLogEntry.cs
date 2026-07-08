namespace ServiceBusEmulatorExplorer.App.ViewModels;

public sealed record OperationLogEntry(DateTimeOffset TimestampUtc, string Message)
{
    public string DisplayText => $"{TimestampUtc:yyyy-MM-ddTHH:mm:ss.fff'Z'} {Message}";
}
