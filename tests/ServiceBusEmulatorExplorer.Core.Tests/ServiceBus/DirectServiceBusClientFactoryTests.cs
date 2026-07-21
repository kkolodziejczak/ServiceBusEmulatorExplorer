using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Tests.ServiceBus;

public sealed class DirectServiceBusClientFactoryTests
{
    [Fact]
    public async Task CreateClients_constructs_azure_cli_clients_without_connecting()
    {
        var profile = new ConnectionProfile(
            "Azure",
            "ignored runtime string",
            "ignored administration string",
            ConnectionAuthenticationMode.AzureCli,
            "orders.servicebus.windows.net");

        (ServiceBusAdministrationClient administrationClient, ServiceBusClient runtimeClient) = CreateClients(profile);

        try
        {
            Assert.IsType<ServiceBusAdministrationClient>(administrationClient);
            Assert.IsType<ServiceBusClient>(runtimeClient);
        }
        finally
        {
            await runtimeClient.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateClients_constructs_connection_string_clients_without_connecting()
    {
        var profile = new ConnectionProfile(
            "Emulator",
            "Endpoint=sb://runtime.example;SharedAccessKeyName=key;SharedAccessKey=value;",
            "Endpoint=sb://admin.example;SharedAccessKeyName=key;SharedAccessKey=value;");

        (ServiceBusAdministrationClient administrationClient, ServiceBusClient runtimeClient) = CreateClients(profile);

        try
        {
            Assert.IsType<ServiceBusAdministrationClient>(administrationClient);
            Assert.IsType<ServiceBusClient>(runtimeClient);
        }
        finally
        {
            await runtimeClient.DisposeAsync();
        }
    }

    private static (ServiceBusAdministrationClient AdministrationClient, ServiceBusClient RuntimeClient) CreateClients(ConnectionProfile profile) =>
        DirectServiceBusClientFactory.CreateClients(profile);
}
