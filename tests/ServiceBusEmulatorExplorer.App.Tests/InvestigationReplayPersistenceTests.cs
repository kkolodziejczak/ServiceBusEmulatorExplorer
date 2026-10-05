using System.IO;
using System.Reflection;
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
    public async Task Protected_round_trip_preserves_attempt_history_per_profile_without_plaintext_identity_or_credentials()
    {
        using var fixture = new PreferencesFile();
        var firstFamily = Family("first", "private-order-123", EntityKind.Queue);
        var secondFamily = Family("second", "private-invoice-456", EntityKind.Subscription);
        var first = Attempt("first", firstFamily, "first-replay-1", ReplaySendStatus.Confirmed) with { HiddenFromHistory = true, SendFailure = ReplaySendFailure.Timeout };
        var second = Attempt("second", secondFamily, "second-replay-2", ReplaySendStatus.Uncertain);
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>>
        {
            ["first"] = [firstFamily],
            ["second"] = [secondFamily]
        }) with { ReplayAttempts = [first, second] };

        await new ProtectedWorkspacePreferencesStore(fixture.Path).SaveAsync(preferences, CancellationToken.None);
        string persisted = await File.ReadAllTextAsync(fixture.Path);
        foreach (string privateValue in new[]
                 {
                     first.OriginalMessageId, second.OriginalMessageId,
                     first.OriginalFingerprint, second.OriginalFingerprint,
                     first.Reservation.MessageId, second.Reservation.MessageId,
                     ConnectionProfileDefaults.LocalEmulator.RuntimeConnectionString,
                     "private-body-content"
                 })
            Assert.DoesNotContain(privateValue, persisted, StringComparison.Ordinal);

        var loaded = await new ProtectedWorkspacePreferencesStore(fixture.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(new[] { first, second }, loaded.Preferences.ReplayAttempts);
        Assert.Equal(first, Assert.Single(loaded.Preferences.ReplayAttempts, item => item.ProfileId == "first"));
        Assert.Equal(second, Assert.Single(loaded.Preferences.ReplayAttempts, item => item.ProfileId == "second"));
    }

    [Fact]
    public async Task Protected_attempt_without_hidden_flag_loads_as_visible()
    {
        using var fixture = new PreferencesFile();
        var family = Family("first", "private-order-123", EntityKind.Queue);
        var attempt = Attempt("first", family, "first-replay-1", ReplaySendStatus.Confirmed);
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>> { ["first"] = [family] })
            with { ReplayAttempts = [attempt] };
        await new ProtectedWorkspacePreferencesStore(fixture.Path).SaveAsync(preferences, CancellationToken.None);

        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
        var attempts = JsonNode.Parse(ProtectedText("Unprotect", envelope["settings"]!["replayAttempts"]!.GetValue<string>()))!;
        attempts[0]!.AsObject().Remove("hiddenFromHistory");
        envelope["settings"]!["replayAttempts"] = ProtectedText("Protect", attempts.ToJsonString());
        await File.WriteAllTextAsync(fixture.Path, envelope.ToJsonString());

        var loaded = await new ProtectedWorkspacePreferencesStore(fixture.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.False(Assert.Single(loaded.Preferences.ReplayAttempts).HiddenFromHistory);
    }

    private static string ProtectedText(string operation, string value)
    {
        var type = typeof(ProtectedWorkspacePreferencesStore).Assembly.GetType(
            "ServiceBusEmulatorExplorer.App.Investigation.Settings.UserProtectedText", throwOnError: true)!;
        return (string)type.GetMethod(operation, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [value])!;
    }

    [Fact]
    public async Task RemovingProfileFromSavedPreferencesDropsOnlyItsReplayAttempts()
    {
        using var fixture = new PreferencesFile();
        var firstFamily = Family("first", "private-order-123", EntityKind.Queue);
        var secondFamily = Family("second", "private-invoice-456", EntityKind.Subscription);
        var first = Attempt("first", firstFamily, "first-replay-1", ReplaySendStatus.Confirmed);
        var second = Attempt("second", secondFamily, "second-replay-2", ReplaySendStatus.Uncertain);
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>>
        {
            ["first"] = [firstFamily],
            ["second"] = [secondFamily]
        }) with
        {
            Profiles = [new InvestigationProfile("second", ConnectionProfileDefaults.LocalEmulator)],
            SelectedProfileId = "second",
            ReplayAttempts = [first, second]
        };

        await new ProtectedWorkspacePreferencesStore(fixture.Path).SaveAsync(preferences, CancellationToken.None);
        var loaded = await new ProtectedWorkspacePreferencesStore(fixture.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal("second", Assert.Single(loaded.Preferences.Profiles).Id);
        Assert.Equal(second, Assert.Single(loaded.Preferences.ReplayAttempts));
        Assert.DoesNotContain(loaded.Preferences.ReplayAttempts, attempt => attempt.ProfileId == "first");
        Assert.False(loaded.Preferences.ReplayFamilies.ContainsKey("first"));
        Assert.Equal(secondFamily, Assert.Single(loaded.Preferences.ReplayFamilies["second"]));
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
        envelope["settings"]!.AsObject().Remove("replayAttempts");
        await File.WriteAllTextAsync(fixture.Path, envelope.ToJsonString());

        var loaded = await new ProtectedWorkspacePreferencesStore(fixture.Path).LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Empty(loaded.Preferences.ReplayFamilies);
        Assert.Empty(loaded.Preferences.ReplayAttempts);
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

    [Theory]
    [InlineData("invalid-original-status")]
    [InlineData("null-locations")]
    [InlineData("null-location")]
    [InlineData("invalid-source")]
    [InlineData("invalid-bucket")]
    [InlineData("invalid-state")]
    [InlineData("missing-check-time")]
    [InlineData("negative-source-count")]
    [InlineData("negative-scanned-count")]
    [InlineData("complete-with-limitation")]
    public async Task Invalid_replay_observation_is_rejected_before_any_file_is_written(string failure)
    {
        using var fixture = new PreferencesFile();
        var family = Family("first", "original", EntityKind.Queue);
        var attempt = Attempt("first", family, "replay-1", ReplaySendStatus.Confirmed);
        var observation = new ReplayObservation(DateTimeOffset.Parse("2026-10-05T10:15:00Z"), true, 1, 0, []);
        var invalid = failure switch
        {
            "invalid-original-status" => attempt with { OriginalStatus = (ReplayOriginalStatus)999 },
            "null-locations" => attempt with { Observation = observation with { Locations = null! } },
            "null-location" => attempt with { Observation = observation with { Locations = [null!] } },
            "invalid-source" => attempt with { Observation = observation with
                { Locations = [new ReplayLocation(new EntityAddress(EntityKind.Topic, "orders"), MessageBucket.Active, "Main queue")] } },
            "invalid-bucket" => attempt with { Observation = observation with
                { Locations = [new ReplayLocation(family.OriginalSource, (MessageBucket)999, "Main queue")] } },
            "invalid-state" => attempt with { Observation = observation with
                { Locations = [new ReplayLocation(family.OriginalSource, MessageBucket.Active, " ")] } },
            "missing-check-time" => attempt with { Observation = observation with { CheckedAtUtc = default } },
            "negative-source-count" => attempt with { Observation = observation with { SourceCount = -1 } },
            "negative-scanned-count" => attempt with { Observation = observation with { ScannedDeliveries = -1 } },
            "complete-with-limitation" => attempt with { Observation = observation with { Limitation = "Scan incomplete." } },
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var preferences = Preferences(new Dictionary<string, IReadOnlyList<ReplayFamilyState>>()) with
        {
            ReplayAttempts = [invalid]
        };

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

    private static ReplayFamilyState Family(string profileId, string originalId, EntityKind kind)
    {
        string root = Fingerprint(profileId + "-root");
        return new ReplayFamilyState(Guid.NewGuid(), root, originalId,
            kind == EntityKind.Queue
                ? new EntityAddress(EntityKind.Queue, profileId + "-orders")
                : new EntityAddress(EntityKind.Subscription, "billing", profileId + "-orders"), 1);
    }

    private static ReplayAttempt Attempt(string profileId, ReplayFamilyState family, string messageId,
        ReplaySendStatus status)
    {
        var reservation = new ReplayReservation(family, messageId);
        return new ReplayAttempt(Guid.NewGuid(), profileId, Fingerprint(profileId + "-namespace"), reservation,
            family.OriginalSource, 42, Fingerprint(profileId + "-original"), family.OriginalMessageId,
            DateTimeOffset.Parse("2026-10-05T10:15:00Z"), status,
            status == ReplaySendStatus.Confirmed ? DateTimeOffset.Parse("2026-10-05T10:15:01Z") : null);
    }

    private static string Fingerprint(string input) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(input)));

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
