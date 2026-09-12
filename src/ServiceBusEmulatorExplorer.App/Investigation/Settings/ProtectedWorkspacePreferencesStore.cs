using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

/// <summary>
/// Stores the investigation profile and workspace preferences in the user's local profile.
/// Connection strings are protected with the current Windows user's DPAPI key.
/// </summary>
public sealed class ProtectedWorkspacePreferencesStore : IWorkspacePreferencesStore
{
    private const int CurrentVersion = 1;
    private const int DefaultPageSize = 50;
    private const int DefaultSearchTimeBudgetSeconds = 30;
    private const int DefaultSearchDeliveryBudget = 10_000;
    private const double DefaultWindowWidth = 1500;
    private const double DefaultWindowHeight = 1000;
    private const string DefaultColor = "#0069FA";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly string filePath;

    public ProtectedWorkspacePreferencesStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
    }

    public static ProtectedWorkspacePreferencesStore CreateDefault()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ServiceBusEmulatorExplorer");

        return new ProtectedWorkspacePreferencesStore(Path.Combine(directory, "connection-profiles.json"));
    }

    public async Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return new PreferencesLoadResult(DefaultPreferences());

        try
        {
            using JsonDocument document = await ReadDocumentAsync(cancellationToken);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                WorkspacePreferences migrated = LoadLegacyProfiles(document.RootElement);
                try
                {
                    await SaveCoreAsync(migrated, cancellationToken);
                    return new PreferencesLoadResult(migrated);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (IsStorageFailure(exception))
                {
                    return new PreferencesLoadResult(migrated, MigrationWarning);
                }
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException();

            StoredEnvelope envelope = document.RootElement.Deserialize<StoredEnvelope>(JsonOptions)
                ?? throw new InvalidDataException();
            return new PreferencesLoadResult(FromStoredEnvelope(envelope));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return new PreferencesLoadResult(DefaultPreferences(), LoadWarning);
        }
    }

    private async Task<JsonDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            await SaveCoreAsync(preferences, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // Do not expose an exception or any of its data to the activity log/UI.
            throw new InvalidOperationException(SaveWarning);
        }
    }

    private async Task SaveCoreAsync(WorkspacePreferences preferences, CancellationToken cancellationToken)
    {
        StoredEnvelope envelope = ToStoredEnvelope(preferences, cancellationToken);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        cancellationToken.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidDataException();

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, ".connection-profiles-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceAtomically(temporaryPath, filePath);
            temporaryPath = string.Empty;
        }
        finally
        {
            if (temporaryPath.Length > 0)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (IsStorageFailure(exception)) { }
            }
        }
    }

    private static void ReplaceAtomically(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
        else
            File.Move(temporaryPath, destinationPath);
    }

    private static WorkspacePreferences LoadLegacyProfiles(JsonElement root)
    {
        List<LegacyProfile>? profiles = root.Deserialize<List<LegacyProfile>>(JsonOptions);
        if (profiles is not { Count: > 0 })
            return DefaultPreferences();

        var mapped = new List<InvestigationProfile>(profiles.Count);
        var seenProfiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (LegacyProfile? profile in profiles)
        {
            if (profile is null)
                throw new InvalidDataException();

            ConnectionAuthenticationMode authenticationMode = profile.AuthenticationMode ?? ConnectionAuthenticationMode.ConnectionString;
            if (authenticationMode is not (ConnectionAuthenticationMode.ConnectionString or ConnectionAuthenticationMode.AzureCli))
                throw new InvalidDataException();

            string name = profile.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new InvalidDataException();

            string runtime = authenticationMode == ConnectionAuthenticationMode.AzureCli
                ? string.Empty
                : profile.RuntimeConnectionString ?? string.Empty;
            string administration = authenticationMode == ConnectionAuthenticationMode.AzureCli
                ? string.Empty
                : profile.AdministrationConnectionString ?? string.Empty;
            string namespaceName = authenticationMode == ConnectionAuthenticationMode.AzureCli
                ? profile.FullyQualifiedNamespace?.Trim() ?? string.Empty
                : string.Empty;

            string identity = string.Join("\u001f", name, runtime, administration, authenticationMode, namespaceName);
            if (!seenProfiles.Add(identity))
                continue;

            string id = IsLocalEmulator(name, runtime, administration, authenticationMode, namespaceName)
                ? "local-emulator"
                : "profile-" + ComputeIdentityHash(identity);
            mapped.Add(new InvestigationProfile(id, new ConnectionProfile(
                name, runtime, administration, authenticationMode, namespaceName), DefaultColor));
        }

        if (mapped.Count == 0)
            return DefaultPreferences();

        return new WorkspacePreferences
        {
            Profiles = mapped,
            SelectedProfileId = mapped[0].Id
        };
    }

    private static bool IsLocalEmulator(string name, string runtime, string administration,
        ConnectionAuthenticationMode authenticationMode, string namespaceName) =>
        string.Equals(name, ConnectionProfileDefaults.LocalEmulator.Name, StringComparison.Ordinal) &&
        string.Equals(runtime, ConnectionProfileDefaults.LocalEmulator.RuntimeConnectionString, StringComparison.Ordinal) &&
        string.Equals(administration, ConnectionProfileDefaults.LocalEmulator.AdministrationConnectionString, StringComparison.Ordinal) &&
        authenticationMode == ConnectionAuthenticationMode.ConnectionString && namespaceName.Length == 0;

    private static string ComputeIdentityHash(string identity)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static WorkspacePreferences FromStoredEnvelope(StoredEnvelope envelope)
    {
        if (envelope.Version != CurrentVersion || envelope.Profiles is not { Count: > 0 } || envelope.Settings is null)
            throw new InvalidDataException();

        var profiles = new List<InvestigationProfile>(envelope.Profiles.Count);
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (StoredProfile? stored in envelope.Profiles)
        {
            if (stored is null || !ValidProfileId(stored.Id) || !profileIds.Add(stored.Id) ||
                string.IsNullOrWhiteSpace(stored.Name) || !ValidColor(stored.ColorHex))
                throw new InvalidDataException();

            ConnectionAuthenticationMode authenticationMode = stored.AuthenticationMode;
            if (authenticationMode is not (ConnectionAuthenticationMode.ConnectionString or ConnectionAuthenticationMode.AzureCli))
                throw new InvalidDataException();

            string runtime = authenticationMode == ConnectionAuthenticationMode.AzureCli
                ? string.Empty
                : UserProtectedText.Unprotect(stored.RuntimeConnection);
            string administration = authenticationMode == ConnectionAuthenticationMode.AzureCli
                ? string.Empty
                : UserProtectedText.Unprotect(stored.AdministrationConnection);
            string namespaceName = authenticationMode == ConnectionAuthenticationMode.AzureCli
                ? stored.FullyQualifiedNamespace?.Trim() ?? string.Empty
                : string.Empty;
            profiles.Add(new InvestigationProfile(
                stored.Id,
                new ConnectionProfile(stored.Name.Trim(), runtime, administration, authenticationMode, namespaceName),
                stored.ColorHex.ToUpperInvariant(),
                stored.WarningMessage ?? string.Empty));
        }

        StoredSettings settings = envelope.Settings;
        string selected = settings.SelectedProfileId is not null && profileIds.Contains(settings.SelectedProfileId)
            ? settings.SelectedProfileId
            : profiles[0].Id;
        return new WorkspacePreferences
        {
            Profiles = profiles,
            SelectedProfileId = selected,
            CloseToTray = settings.CloseToTray,
            NotificationsEnabled = settings.NotificationsEnabled,
            AutoConnectOnSwitch = settings.AutoConnectOnSwitch,
            WasConnected = settings.WasConnected,
            LogExpanded = settings.LogExpanded,
            TimestampDisplay = ValidTimestamp(settings.TimestampDisplay) ? settings.TimestampDisplay : TimestampDisplay.Utc,
            QueuePageSize = ValidPageSize(settings.QueuePageSize) ? settings.QueuePageSize : DefaultPageSize,
            TopicPageSize = ValidPageSize(settings.TopicPageSize) ? settings.TopicPageSize : DefaultPageSize,
            SubscriptionPageSize = ValidPageSize(settings.SubscriptionPageSize) ? settings.SubscriptionPageSize : DefaultPageSize,
            SearchTimeBudgetSeconds = ValidSearchTimeBudgetSeconds(settings.SearchTimeBudgetSeconds)
                ? settings.SearchTimeBudgetSeconds
                : DefaultSearchTimeBudgetSeconds,
            SearchDeliveryBudget = ValidSearchDeliveryBudget(settings.SearchDeliveryBudget)
                ? settings.SearchDeliveryBudget
                : DefaultSearchDeliveryBudget,
            AutoRefreshSeconds = settings.AutoRefreshSeconds >= 0 ? settings.AutoRefreshSeconds : 0,
            SelectedEntityPath = settings.SelectedEntityPath ?? string.Empty,
            DeadLetter = settings.DeadLetter,
            WindowWidth = ValidWindowSize(settings.WindowWidth, 980) ? settings.WindowWidth : DefaultWindowWidth,
            WindowHeight = ValidWindowSize(settings.WindowHeight, 640) ? settings.WindowHeight : DefaultWindowHeight,
            Watches = ReadWatches(settings.Watches, profileIds)
        };
    }

    private static StoredEnvelope ToStoredEnvelope(WorkspacePreferences preferences, CancellationToken cancellationToken)
    {
        IReadOnlyList<InvestigationProfile> profiles = preferences.Profiles ?? [];
        if (profiles.Count == 0)
            profiles = [new InvestigationProfile("local-emulator", ConnectionProfileDefaults.LocalEmulator)];

        var storedProfiles = new List<StoredProfile>(profiles.Count);
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (InvestigationProfile? profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (profile is null || profile.Connection is null || !ValidProfileId(profile.Id) || !profileIds.Add(profile.Id) ||
                string.IsNullOrWhiteSpace(profile.Connection.Name) || !ValidColor(profile.ColorHex))
                throw new InvalidDataException();

            ConnectionProfile connection = profile.Connection;
            if (connection.AuthenticationMode is not (ConnectionAuthenticationMode.ConnectionString or ConnectionAuthenticationMode.AzureCli))
                throw new InvalidDataException();

            storedProfiles.Add(new StoredProfile(
                profile.Id,
                connection.Name.Trim(),
                profile.ColorHex.ToUpperInvariant(),
                connection.AuthenticationMode,
                connection.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
                    ? null
                    : UserProtectedText.Protect(connection.RuntimeConnectionString ?? string.Empty),
                connection.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
                    ? null
                    : UserProtectedText.Protect(connection.AdministrationConnectionString ?? string.Empty),
                connection.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
                    ? connection.FullyQualifiedNamespace?.Trim() ?? string.Empty
                    : null,
                profile.WarningMessage ?? string.Empty));
        }

        string selected = preferences.SelectedProfileId is not null && profileIds.Contains(preferences.SelectedProfileId)
            ? preferences.SelectedProfileId
            : storedProfiles[0].Id;
        return new StoredEnvelope(
            CurrentVersion,
            storedProfiles,
            new StoredSettings(
                selected,
                preferences.CloseToTray,
                preferences.NotificationsEnabled,
                preferences.AutoConnectOnSwitch,
                preferences.WasConnected,
                preferences.LogExpanded,
                ValidTimestamp(preferences.TimestampDisplay) ? preferences.TimestampDisplay : TimestampDisplay.Utc,
                ValidPageSize(preferences.QueuePageSize) ? preferences.QueuePageSize : DefaultPageSize,
                ValidPageSize(preferences.TopicPageSize) ? preferences.TopicPageSize : DefaultPageSize,
                ValidPageSize(preferences.SubscriptionPageSize) ? preferences.SubscriptionPageSize : DefaultPageSize,
                ValidSearchTimeBudgetSeconds(preferences.SearchTimeBudgetSeconds)
                    ? preferences.SearchTimeBudgetSeconds
                    : DefaultSearchTimeBudgetSeconds,
                ValidSearchDeliveryBudget(preferences.SearchDeliveryBudget)
                    ? preferences.SearchDeliveryBudget
                    : DefaultSearchDeliveryBudget,
                preferences.AutoRefreshSeconds >= 0 ? preferences.AutoRefreshSeconds : 0,
                preferences.SelectedEntityPath ?? string.Empty,
                preferences.DeadLetter,
                ValidWindowSize(preferences.WindowWidth, 980) ? preferences.WindowWidth : DefaultWindowWidth,
                ValidWindowSize(preferences.WindowHeight, 640) ? preferences.WindowHeight : DefaultWindowHeight,
                WriteWatches(preferences.Watches, profileIds)));
    }

    private static WorkspacePreferences DefaultPreferences() => new()
    {
        Profiles = [new InvestigationProfile("local-emulator", ConnectionProfileDefaults.LocalEmulator)],
        SelectedProfileId = "local-emulator"
    };

    private static Dictionary<string, IReadOnlyList<WatchPreference>> ReadWatches(
        Dictionary<string, List<StoredWatch>>? source, HashSet<string> profileIds)
    {
        var result = new Dictionary<string, IReadOnlyList<WatchPreference>>(StringComparer.Ordinal);
        if (source is null)
            return result;

        foreach ((string profileId, List<StoredWatch>? rules) in source)
        {
            if (!profileIds.Contains(profileId) || rules is null)
                continue;

            var restored = new List<WatchPreference>(rules.Count);
            foreach (StoredWatch? rule in rules)
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.ScopeKey))
                    continue;
                restored.Add(new WatchPreference(rule.ScopeKey, rule.Active, rule.DeadLetter, rule.Included));
            }
            if (restored.Count > 0)
                result[profileId] = restored;
        }
        return result;
    }

    private static Dictionary<string, List<StoredWatch>> WriteWatches(
        IReadOnlyDictionary<string, IReadOnlyList<WatchPreference>>? source, HashSet<string> profileIds)
    {
        var result = new Dictionary<string, List<StoredWatch>>(StringComparer.Ordinal);
        if (source is null)
            return result;

        foreach ((string profileId, IReadOnlyList<WatchPreference>? rules) in source)
        {
            if (!profileIds.Contains(profileId) || rules is null)
                continue;

            var stored = new List<StoredWatch>(rules.Count);
            foreach (WatchPreference? rule in rules)
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.ScopeKey))
                    continue;
                stored.Add(new StoredWatch(rule.ScopeKey, rule.Active, rule.DeadLetter, rule.Included));
            }
            if (stored.Count > 0)
                result[profileId] = stored;
        }
        return result;
    }

    private static bool ValidProfileId(string? value) => value is { Length: >= 1 and <= 128 } &&
        char.IsLetterOrDigit(value[0]) && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' &&
        value.Skip(1).All(Uri.IsHexDigit);

    private static bool ValidPageSize(int value) => value is 25 or 50 or 100 or 200;

    private static bool ValidSearchTimeBudgetSeconds(int value) => value is 10 or 30 or 60 or 120;

    private static bool ValidSearchDeliveryBudget(int value) => value is 1_000 or 10_000 or 50_000 or 100_000;

    private static bool ValidTimestamp(TimestampDisplay value) => value is TimestampDisplay.Utc or TimestampDisplay.Local;

    private static bool ValidWindowSize(double value, double minimum) => double.IsFinite(value) && value >= minimum;

    private static bool IsStorageFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        JsonException or CryptographicException or Win32Exception or FormatException or ArgumentException or
        NotSupportedException or System.Security.SecurityException or InvalidDataException or OverflowException;

    private const string LoadWarning = "Saved preferences could not be loaded. Defaults are in use; connection secrets may belong to another Windows user.";
    private const string MigrationWarning = "Saved profiles were loaded, but migration to protected storage could not be completed. The original file was preserved.";
    private const string SaveWarning = "Preferences could not be saved. Changes remain available in this session.";

    private sealed record StoredEnvelope(int Version, List<StoredProfile> Profiles, StoredSettings Settings);

    private sealed record StoredProfile(
        string Id,
        string Name,
        string ColorHex,
        ConnectionAuthenticationMode AuthenticationMode,
        string? RuntimeConnection,
        string? AdministrationConnection,
        string? FullyQualifiedNamespace,
        string WarningMessage);

    private sealed record StoredSettings(
        string? SelectedProfileId,
        bool CloseToTray,
        bool NotificationsEnabled,
        bool AutoConnectOnSwitch,
        bool WasConnected,
        bool LogExpanded,
        TimestampDisplay TimestampDisplay,
        int QueuePageSize,
        int TopicPageSize,
        int SubscriptionPageSize,
        int SearchTimeBudgetSeconds,
        int SearchDeliveryBudget,
        int AutoRefreshSeconds,
        string SelectedEntityPath,
        bool DeadLetter,
        double WindowWidth,
        double WindowHeight,
        Dictionary<string, List<StoredWatch>> Watches);

    private sealed record StoredWatch(string ScopeKey, bool? Active, bool? DeadLetter, bool? Included = null);

    private sealed record LegacyProfile(
        string? Name,
        string? RuntimeConnectionString,
        string? AdministrationConnectionString,
        ConnectionAuthenticationMode? AuthenticationMode,
        string? FullyQualifiedNamespace);
}

internal static class UserProtectedText
{
    public static string Protect(string text) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text), protect: true));

    public static string Unprotect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException();
        return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(text), protect: false));
    }

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (var index = 0; index < input.Length; index++)
                Marshal.WriteByte(input.Data, index, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var index = 0; index < output.Length; index++)
                    Marshal.WriteByte(output.Data, index, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
