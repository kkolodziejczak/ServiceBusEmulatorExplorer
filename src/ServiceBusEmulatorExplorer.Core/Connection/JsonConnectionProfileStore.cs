using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBusEmulatorExplorer.Core.Connection;

public sealed class JsonConnectionProfileStore(string filePath) : IConnectionProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static JsonConnectionProfileStore CreateDefault()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ServiceBusEmulatorExplorer");

        return new JsonConnectionProfileStore(Path.Combine(directory, "connection-profiles.json"));
    }

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [ConnectionProfileDefaults.LocalEmulator];
        }

        await using FileStream stream = File.OpenRead(filePath);
        var serializedProfiles = await JsonSerializer.DeserializeAsync<List<SerializedConnectionProfile>>(
            stream,
            SerializerOptions,
            cancellationToken);

        IReadOnlyList<ConnectionProfile> profiles = serializedProfiles?
            .Select(FromSerializedProfile)
            .ToArray() ?? [];

        return profiles.Count > 0
            ? profiles
            : [ConnectionProfileDefaults.LocalEmulator];
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(
            stream,
            profiles.Select(ToSerializedProfile),
            SerializerOptions,
            cancellationToken);
    }

    private static ConnectionProfile FromSerializedProfile(SerializedConnectionProfile profile)
    {
        return new ConnectionProfile(
            profile.Name ?? "",
            profile.RuntimeConnectionString ?? "",
            profile.AdministrationConnectionString ?? "",
            profile.AuthenticationMode ?? ConnectionAuthenticationMode.ConnectionString,
            profile.FullyQualifiedNamespace ?? "");
    }

    private static SerializedConnectionProfile ToSerializedProfile(ConnectionProfile profile)
    {
        return profile.AuthenticationMode == ConnectionAuthenticationMode.AzureCli
            ? new SerializedConnectionProfile(
                profile.Name,
                null,
                null,
                profile.AuthenticationMode,
                profile.FullyQualifiedNamespace)
            : new SerializedConnectionProfile(
                profile.Name,
                profile.RuntimeConnectionString,
                profile.AdministrationConnectionString,
                profile.AuthenticationMode,
                null);
    }

    private sealed record SerializedConnectionProfile(
        string? Name,
        string? RuntimeConnectionString,
        string? AdministrationConnectionString,
        ConnectionAuthenticationMode? AuthenticationMode,
        string? FullyQualifiedNamespace);
}
