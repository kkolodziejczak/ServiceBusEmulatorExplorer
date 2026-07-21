using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.Tests.Connection;

public sealed class JsonConnectionProfileStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_default_profile_when_store_does_not_exist()
    {
        string filePath = CreateTempFilePath();
        var store = new JsonConnectionProfileStore(filePath);

        IReadOnlyList<ConnectionProfile> profiles = await store.LoadAsync(CancellationToken.None);

        Assert.Single(profiles);
        Assert.Equal(ConnectionProfileDefaults.LocalEmulator, profiles[0]);
    }

    [Fact]
    public async Task SaveAsync_persists_profiles_that_LoadAsync_can_read()
    {
        string filePath = CreateTempFilePath();
        var store = new JsonConnectionProfileStore(filePath);
        var profile = new ConnectionProfile(
            "Saved",
            "Endpoint=sb://localhost;UseDevelopmentEmulator=true;",
            "Endpoint=sb://localhost:5300;UseDevelopmentEmulator=true;");

        await store.SaveAsync([profile], CancellationToken.None);
        IReadOnlyList<ConnectionProfile> profiles = await store.LoadAsync(CancellationToken.None);

        Assert.Equal([profile], profiles);
    }

    [Fact]
    public async Task LoadAsync_loads_legacy_three_field_json_as_connection_string_mode()
    {
        string filePath = CreateTempFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, """
            [{"name":"Legacy","runtimeConnectionString":"Endpoint=sb://runtime;","administrationConnectionString":"Endpoint=sb://admin;"}]
            """);
        var store = new JsonConnectionProfileStore(filePath);

        IReadOnlyList<ConnectionProfile> profiles = await store.LoadAsync(CancellationToken.None);

        Assert.Single(profiles);
        Assert.Equal(ConnectionAuthenticationMode.ConnectionString, profiles[0].AuthenticationMode);
        Assert.Equal("", profiles[0].FullyQualifiedNamespace);
    }

    [Fact]
    public async Task SaveAsync_round_trips_azure_cli_profile_without_a_token()
    {
        string filePath = CreateTempFilePath();
        var store = new JsonConnectionProfileStore(filePath);
        var profile = new ConnectionProfile(
            "Azure",
            "Endpoint=sb://runtime;SharedAccessKey=secret;",
            "Endpoint=sb://admin;SharedAccessKey=secret;",
            ConnectionAuthenticationMode.AzureCli,
            "orders.servicebus.windows.net");

        await store.SaveAsync([profile], CancellationToken.None);
        string json = await File.ReadAllTextAsync(filePath);
        IReadOnlyList<ConnectionProfile> profiles = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("", profiles[0].RuntimeConnectionString);
        Assert.Equal("", profiles[0].AdministrationConnectionString);
        Assert.Equal("orders.servicebus.windows.net", profiles[0].FullyQualifiedNamespace);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtimeConnectionString", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("administrationConnectionString", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_maps_explicit_null_profile_values_to_empty_strings()
    {
        string filePath = CreateTempFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, """
            [{"name":"Azure","authenticationMode":1,"fullyQualifiedNamespace":null}]
            """);
        var store = new JsonConnectionProfileStore(filePath);

        IReadOnlyList<ConnectionProfile> profiles = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("", profiles[0].FullyQualifiedNamespace);
        Assert.False(ConnectionProfileValidator.Validate(profiles[0]).IsValid);
    }

    private static string CreateTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "sbe-tests", Guid.NewGuid().ToString("N"), "profiles.json");
    }
}
