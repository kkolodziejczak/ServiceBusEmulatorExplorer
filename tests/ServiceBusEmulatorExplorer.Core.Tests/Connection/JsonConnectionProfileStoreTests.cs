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

    private static string CreateTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "sbe-tests", Guid.NewGuid().ToString("N"), "profiles.json");
    }
}
