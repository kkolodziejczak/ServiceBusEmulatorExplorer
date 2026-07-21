namespace ServiceBusEmulatorExplorer.Core.Connection;

public enum ConnectionAuthenticationMode
{
    ConnectionString,
    AzureCli
}

public sealed record ConnectionProfile(
    string Name,
    string RuntimeConnectionString,
    string AdministrationConnectionString,
    ConnectionAuthenticationMode AuthenticationMode = ConnectionAuthenticationMode.ConnectionString,
    string FullyQualifiedNamespace = "");
