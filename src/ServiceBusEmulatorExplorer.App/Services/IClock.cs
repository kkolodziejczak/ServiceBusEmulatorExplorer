namespace ServiceBusEmulatorExplorer.App.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
