namespace ServiceBusEmulatorExplorer.Core.Connection;

public sealed record ConnectionProfile(
    string Name,
    string RuntimeConnectionString,
    string AdministrationConnectionString);
