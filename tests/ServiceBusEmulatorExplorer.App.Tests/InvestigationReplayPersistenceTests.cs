using System.IO;
using System.Text.Json.Nodes;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class InvestigationReplayPersistenceTests
{
    [Fact]
    public async Task Protected_round_trip_preserves_independent_attempts_and_lineage_for_each_saved_profile()
    {
        using var fixture = new PreferencesFile();
        var familyId = Guid.NewGuid();
        var first = new ReplayFamilyState(familyId, new string('A', 64), "private-order-123",
            new EntityAddress(EntityKind.Queue, "orders"), 3);
        var second = new ReplayFamilyState(familyId, new string('B', 64), "private-invoice-456",
            new EntityAddress(EntityKind.Subscription, "billing", "order-events"), 11);
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>>
        {
            ["first"] = [first],
            ["second"] = [second]
        });

        await new ProtectedWorkspacePreferencesStore(fixture.Path).SaveAsync(preferences, CancellationToken.None);
        string persisted = await File.ReadAllTextAsync(fixture.Path);
        foreach (var family in new[] { first, second })
        {
            Assert.DoesNotContain(family.OriginalMessageId, persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(family.RootFingerprint, persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(family.FamilyId.ToString(), persisted, StringComparison.Ordinal);
        }

        var loaded = await new ProtectedWorkspacePreferencesStore(fixture.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(2, loaded.Preferences.ReplayFamilies.Count);
        Assert.Equal(first, Assert.Single(loaded.Preferences.ReplayFamilies["first"]));
        Assert.Equal(second, Assert.Single(loaded.Preferences.ReplayFamilies["second"]));
    }

    [Fact]
    public async Task Older_protected_envelope_without_replay_field_loads_without_losing_other_settings()
    {
        using var fixture = new PreferencesFile();
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>>()) with
        {
            QueuePageSize = 100
        };
        await new ProtectedWorkspacePreferencesStore(fixture.Path).SaveAsync(preferences, CancellationToken.None);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!.AsObject();
        envelope["settings"]!.AsObject().Remove("replayFamilies");
        await File.WriteAllTextAsync(fixture.Path, envelope.ToJsonString());

        var loaded = await new ProtectedWorkspacePreferencesStore(fixture.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Empty(loaded.Preferences.ReplayFamilies);
        Assert.Equal(100, loaded.Preferences.QueuePageSize);
        Assert.Equal(new[] { "first", "second" }, loaded.Preferences.Profiles.Select(profile => profile.Id));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-root")]
    [InlineData("invalid-root")]
    [InlineData("invalid-attempt")]
    [InlineData("invalid-source")]
    public async Task Invalid_family_state_is_rejected_before_any_file_is_written(string failure)
    {
        using var fixture = new PreferencesFile();
        var valid = new ReplayFamilyState(Guid.NewGuid(), new string('A', 64), "original",
            new EntityAddress(EntityKind.Queue, "orders"), 1);
        var invalid = failure switch
        {
            "duplicate-id" => valid with { RootFingerprint = new string('B', 64) },
            "duplicate-root" => valid with { FamilyId = Guid.NewGuid(), RootFingerprint = new string('a', 64) },
            "invalid-root" => valid with { FamilyId = Guid.NewGuid(), RootFingerprint = "not-a-fingerprint" },
            "invalid-attempt" => valid with { FamilyId = Guid.NewGuid(), LastAttempt = 0 },
            "invalid-source" => valid with { FamilyId = Guid.NewGuid(), OriginalSource = new EntityAddress(EntityKind.Topic, "orders") },
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>>
        {
            ["first"] = [valid, invalid]
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProtectedWorkspacePreferencesStore(fixture.Path).SaveAsync(preferences, CancellationToken.None));

        Assert.False(File.Exists(fixture.Path));
    }

    private static WorkspacePreferences Preferences(IReadOnlyDictionary<string, IReadOnlyList<ReplayFamilyState>> families) => new()
    {
        Profiles =
        [
            new InvestigationProfile("first", ConnectionProfileDefaults.LocalEmulator),
            new InvestigationProfile("second", ConnectionProfileDefaults.LocalEmulator)
        ],
        SelectedProfileId = "first",
        ReplayFamilies = families
    };

    private sealed class PreferencesFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "sbe-replay-preferences-" + Guid.NewGuid().ToString("N"), "preferences.json");

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
            string directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }
}
