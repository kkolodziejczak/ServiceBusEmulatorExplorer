using System.IO;
using System.Text.Json;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation.Settings;

public sealed partial class ProtectedWorkspacePreferencesStore
{
    private static IReadOnlyList<ReplayAttempt> ReadReplayAttempts(string? value, HashSet<string> profiles)
    {
        if (string.IsNullOrEmpty(value)) return [];
        var attempts = JsonSerializer.Deserialize<ReplayAttempt[]>(UserProtectedText.Unprotect(value), JsonOptions)
            ?? throw new InvalidDataException("Invalid replay history.");
        ValidateReplayAttempts(attempts);
        return attempts.Where(attempt => profiles.Contains(attempt.ProfileId)).ToArray();
    }

    private static string? WriteReplayAttempts(IReadOnlyList<ReplayAttempt> attempts, HashSet<string> profiles)
    {
        ValidateReplayAttempts(attempts);
        var retained = attempts.Where(attempt => profiles.Contains(attempt.ProfileId)).ToArray();
        return retained.Length == 0 ? null : UserProtectedText.Protect(JsonSerializer.Serialize(retained, JsonOptions));
    }

    private static void ValidateReplayAttempts(IReadOnlyList<ReplayAttempt> attempts)
    {
        if (attempts is null || attempts.Any(attempt => attempt is null))
            throw new InvalidDataException("Invalid replay history.");
        foreach (var attempt in attempts) attempt.Validate();
        if (attempts.Select(attempt => attempt.AttemptId).Distinct().Count() != attempts.Count)
            throw new InvalidDataException("Duplicate replay attempt.");
    }
}
