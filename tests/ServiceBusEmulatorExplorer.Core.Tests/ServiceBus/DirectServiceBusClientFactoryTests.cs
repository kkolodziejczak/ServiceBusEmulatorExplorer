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

    [Theory]
    [InlineData("Endpoint=sb://admin.example;SharedAccessKeyName=key;SharedAccessKey=value;UseDevelopmentEmulator=true;", false)]
    [InlineData("Endpoint=sb://admin.example;SharedAccessKeyName=key;SharedAccessKey=value; UseDevelopmentEmulator = true;", false)]
    [InlineData("Endpoint=sb://admin.example;SharedAccessKeyName=key;SharedAccessKey=value;UseDevelopmentEmulator=false;", true)]
    [InlineData("Endpoint=sb://admin.example;SharedAccessKeyName=key;SharedAccessKey=value;", true)]
    public void RuntimeCountsSupported_reads_only_the_explicit_emulator_flag(string administrationConnectionString, bool expected)
    {
        var emulator = new ConnectionProfile(
            "Emulator",
            "Endpoint=sb://runtime.example;SharedAccessKeyName=key;SharedAccessKey=value;UseDevelopmentEmulator=true;",
            administrationConnectionString);
        var azureCli = new ConnectionProfile(
            "Azure CLI",
            "ignored",
            "ignored",
            ConnectionAuthenticationMode.AzureCli,
            "orders.servicebus.windows.net");

        Assert.Equal(expected, DirectServiceBusClientFactory.RuntimeCountsSupported(emulator));
        Assert.True(DirectServiceBusClientFactory.RuntimeCountsSupported(azureCli));
    }

    [Fact]
    public async Task SupportsRuntimeCounts_starts_true_and_remains_true_after_dispose()
    {
        await using var factory = new DirectServiceBusClientFactory();

        Assert.True(factory.SupportsRuntimeCounts);
        await factory.DisposeAsync();
        Assert.True(factory.SupportsRuntimeCounts);
    }

    private static (ServiceBusAdministrationClient AdministrationClient, ServiceBusClient RuntimeClient) CreateClients(ConnectionProfile profile) =>
        DirectServiceBusClientFactory.CreateClients(profile);
}
