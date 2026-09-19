using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationReplayCleanupPersistenceTests
{
    [Fact]
    public async Task Cleanup_namespace_round_trips_independently_per_profile_inside_encrypted_lineage()
    {
        using var file = new PreferencesFile();
        var first = Family() with { CleanupNamespace = new string('C', 64) };
        var second = first with { CleanupNamespace = new string('D', 64), LastAttempt = 8 };
        var settings = Preferences(first, second);

        await new ProtectedWorkspacePreferencesStore(file.Path).SaveAsync(settings, CancellationToken.None);
        string json = await File.ReadAllTextAsync(file.Path);
        Assert.DoesNotContain(first.CleanupNamespace!, json, StringComparison.Ordinal);
        Assert.DoesNotContain(second.CleanupNamespace!, json, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanupNamespace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(first.OriginalMessageId, json, StringComparison.Ordinal);

        var loaded = await new ProtectedWorkspacePreferencesStore(file.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(first, Assert.Single(loaded.Preferences.ReplayFamilies["first"]));
        Assert.Equal(second, Assert.Single(loaded.Preferences.ReplayFamilies["second"]));
    }

    [Fact]
    public async Task Legacy_encrypted_family_without_cleanup_field_preserves_attempt_and_defaults_to_no_cleanup()
    {
        using var file = new PreferencesFile();
        var original = Family();
        await new ProtectedWorkspacePreferencesStore(file.Path).SaveAsync(Preferences(original, original), CancellationToken.None);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(file.Path))!;
        var settings = envelope["settings"]!;
        var families = JsonNode.Parse(ProtectedText("Unprotect", settings["replayFamilies"]!.GetValue<string>()))!;
        foreach (string profile in new[] { "first", "second" })
            families[profile]![0]!.AsObject().Remove("cleanupNamespace");
        settings["replayFamilies"] = ProtectedText("Protect", families.ToJsonString());
        await File.WriteAllTextAsync(file.Path, envelope.ToJsonString());

        var loaded = await new ProtectedWorkspacePreferencesStore(file.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(original, Assert.Single(loaded.Preferences.ReplayFamilies["first"]));
        Assert.Null(loaded.Preferences.ReplayFamilies["first"][0].CleanupNamespace);
    }

    private static string ProtectedText(string operation, string value)
    {
        var type = typeof(ProtectedWorkspacePreferencesStore).Assembly.GetType(
            "ServiceBusEmulatorExplorer.App.Investigation.Settings.UserProtectedText", throwOnError: true)!;
        return (string)type.GetMethod(operation, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [value])!;
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-namespace-fingerprint")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAG")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Invalid_cleanup_namespace_is_rejected_without_writing_settings(string value)
    {
        using var file = new PreferencesFile();
        var invalid = Family() with { CleanupNamespace = value };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProtectedWorkspacePreferencesStore(file.Path).SaveAsync(Preferences(invalid, Family()), CancellationToken.None));

        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public void Namespace_identity_ignores_credential_rotation_connection_string_order_and_endpoint_casing()
    {
        var first = Profile("Endpoint=sb://ORDERS.servicebus.windows.net/;SharedAccessKeyName=first;SharedAccessKey=old;");
        var rotated = Profile("SharedAccessKey=new;SharedAccessKeyName=second;Endpoint=sb://orders.servicebus.windows.net;") with
        {
            Name = "Renamed profile", AdministrationConnectionString = "unrelated-admin-credential"
        };

        string identity = ReplayNamespace.Fingerprint(first);

        Assert.Equal(64, identity.Length);
        Assert.All(identity, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal(identity, ReplayNamespace.Fingerprint(rotated));
    }

    [Fact]
    public void Azure_cli_and_connection_string_for_same_namespace_have_same_identity()
    {
        var connectionString = Profile("Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=value;");
        var cli = new ConnectionProfile("CLI", "", "", ConnectionAuthenticationMode.AzureCli, " ORDERS.servicebus.windows.net/ ");

        Assert.Equal(ReplayNamespace.Fingerprint(connectionString), ReplayNamespace.Fingerprint(cli));
    }

    [Theory]
    [InlineData("sb://other.servicebus.windows.net/")]
    [InlineData("sb://orders.servicebus.windows.net:5673/")]
    public void Different_broker_host_or_port_cannot_share_cleanup_authorization(string endpoint)
    {
        var first = Profile("Endpoint=sb://orders.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=value;");
        var different = Profile($"Endpoint={endpoint};SharedAccessKeyName=key;SharedAccessKey=value;");

        Assert.NotEqual(ReplayNamespace.Fingerprint(first), ReplayNamespace.Fingerprint(different));
    }

    private static ConnectionProfile Profile(string runtime) => new("Test", runtime, runtime);

    private static ReplayFamilyState Family() => new(Guid.NewGuid(), new string('A', 64), "private-order-123",
        new EntityAddress(EntityKind.Queue, "orders"), 3);

    private static WorkspacePreferences Preferences(ReplayFamilyState first, ReplayFamilyState second) => new()
    {
        Profiles =
        [
            new InvestigationProfile("first", ConnectionProfileDefaults.LocalEmulator),
            new InvestigationProfile("second", ConnectionProfileDefaults.LocalEmulator)
        ],
        SelectedProfileId = "first",
        ReplayFamilies = new Dictionary<string, IReadOnlyList<ReplayFamilyState>>
        {
            ["first"] = [first], ["second"] = [second]
        }
    };

    private sealed class PreferencesFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "sbe-cleanup-preferences-" + Guid.NewGuid().ToString("N"), "preferences.json");

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
            string directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }
}
