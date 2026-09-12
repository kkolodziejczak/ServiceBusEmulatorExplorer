using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public sealed class PrototypePreferences
{
    public PrototypePreferences()
    {
        Profiles = new PrototypeConnectionSettings().Profiles;
        SelectedProfileId = Profiles[0].Id;
    }

    [JsonIgnore] public List<PrototypeConnectionProfile> Profiles { get; set; }
    public string SelectedProfileId { get; set; }
    public Dictionary<string, WatchRulesSnapshot> Watches { get; set; } = [];
    public bool CloseToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool Connected { get; set; } = true;
    public int RefreshIndex { get; set; }
    public bool RefreshPaused { get; set; }
    public bool SearchByMessageId { get; set; }
    public int TimeIndex { get; set; }
    public bool LogExpanded { get; set; } = true;
    public double Width { get; set; } = 1500;
    public double Height { get; set; } = 1000;
    public string SelectedEntityPath { get; set; } = string.Empty;
    public bool DeadLetter { get; set; }
    public string SearchText { get; set; } = string.Empty;
    public string AppliedSearch { get; set; } = string.Empty;
}

public sealed class PrototypePreferencesStore(string? path = null)
{
    private readonly string filePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ServiceBusEmulatorExplorer", "InvestigationPrototype", "preferences.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool TryLoad(out PrototypePreferences preferences, out string? warning)
    {
        preferences = new PrototypePreferences();
        warning = null;
        try
        {
            if (!File.Exists(filePath)) return true;
            var document = JsonSerializer.Deserialize<StoredPreferences>(File.ReadAllText(filePath), JsonOptions);
            if (document is null || document.Version != 1 || document.Settings is null || document.Profiles is not { Count: > 0 } ||
                document.Profiles.Any(profile => profile is null))
                throw new InvalidDataException();
            var profiles = document.Profiles.Select(profile => new PrototypeConnectionProfile(profile.Name,
                UserProtectedText.Unprotect(profile.Runtime), UserProtectedText.Unprotect(profile.Administration))
            {
                Id = profile.Id,
                ColorHex = profile.ColorHex
            }).ToList();
            if (profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || !ValidColor(profile.ColorHex)) ||
                profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count)
                throw new InvalidDataException();
            var loaded = document.Settings;
            loaded.Profiles = profiles;
            if (!profiles.Any(profile => profile.Id == loaded.SelectedProfileId)) loaded.SelectedProfileId = profiles[0].Id;
            loaded.Watches ??= [];
            foreach (var key in loaded.Watches.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray()) loaded.Watches.Remove(key);
            if (loaded.Watches.Values.Any(snapshot => snapshot.Scopes?.Any(rule => rule is null) == true ||
                snapshot.Inclusions?.Any(rule => rule is null) == true)) throw new InvalidDataException();
            loaded.RefreshIndex = Math.Clamp(loaded.RefreshIndex, 0, 3);
            loaded.TimeIndex = Math.Clamp(loaded.TimeIndex, 0, 2);
            if (!double.IsFinite(loaded.Width) || loaded.Width < 980) loaded.Width = 1500;
            if (!double.IsFinite(loaded.Height) || loaded.Height < 640) loaded.Height = 1000;
            loaded.SelectedEntityPath ??= string.Empty;
            loaded.SearchText ??= string.Empty;
            loaded.AppliedSearch ??= string.Empty;
            preferences = loaded;
            return true;
        }
        catch (Exception exception) when (IsPreferenceFailure(exception))
        {
            warning = "Saved preferences could not be loaded. Defaults are in use; connection secrets may belong to another Windows user.";
            return false;
        }
    }

    private static bool ValidColor(string? color) => color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);

    public bool TrySave(PrototypePreferences preferences, out string? warning)
    {
        warning = null;
        string? temporaryPath = null;
        try
        {
            var document = new StoredPreferences(1, preferences, preferences.Profiles.Select(profile => new StoredProfile(
                profile.Id, profile.Name, profile.ColorHex, UserProtectedText.Protect(profile.RuntimeConnection),
                UserProtectedText.Protect(profile.AdministrationConnection))).ToList());
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, ".preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(output, document, JsonOptions);
                output.Flush(true);
            }
            File.Move(temporaryPath, fullPath, true);
            temporaryPath = null;
            return true;
        }
        catch (Exception exception) when (IsPreferenceFailure(exception))
        {
            warning = "Preferences could not be saved. Changes remain available in this session.";
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (IsPreferenceFailure(exception)) { }
        }
    }

    private static bool IsPreferenceFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        JsonException or CryptographicException or Win32Exception or FormatException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private sealed record StoredPreferences(int Version, PrototypePreferences Settings, List<StoredProfile> Profiles);
    private sealed record StoredProfile(string Id, string Name, string ColorHex, string Runtime, string Administration);
}

internal static class UserProtectedText
{
    public static string Protect(string text) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text), true));
    public static string Unprotect(string text) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(text), false));

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            // CRYPTPROTECT_UI_FORBIDDEN, current Windows user scope (no machine-wide flag).
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (var index = 0; index < input.Length; index++) Marshal.WriteByte(input.Data, index, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var index = 0; index < output.Length; index++) Marshal.WriteByte(output.Data, index, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
