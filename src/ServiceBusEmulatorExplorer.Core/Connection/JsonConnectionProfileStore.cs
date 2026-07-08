using System.Text.Json;

namespace ServiceBusEmulatorExplorer.Core.Connection;

public sealed class JsonConnectionProfileStore(string filePath) : IConnectionProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
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
        var profiles = await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(
            stream,
            SerializerOptions,
            cancellationToken);

        return profiles is { Count: > 0 }
            ? profiles
            : [ConnectionProfileDefaults.LocalEmulator];
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, profiles, SerializerOptions, cancellationToken);
    }
}
