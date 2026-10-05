using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record WorkspaceDeleteResult(DlqDeleteResult Deletion, bool CleanupPersistenceFailed);

public sealed partial class InvestigationWorkspace
{
    public async Task<WorkspaceDeleteResult> DeleteAsync(IReadOnlyList<MessageDelivery> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        cancellationToken.ThrowIfCancellationRequested();
        await StopReplayCleanupAsync();
        try { await mutationGate.WaitAsync(cancellationToken); }
        catch { StartReplayCleanup(); throw; }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        mutationCancellation = operation;
        try
        {
            return await DeleteCapturedAsync(targets, operation.Token);
        }
        finally
        {
            mutationCancellation = null;
            mutationGate.Release();
            StartReplayCleanup();
        }
    }

    // Both deletion entry points hold mutationGate; all settlement and presentation updates stay canonical here.
    private async Task<WorkspaceDeleteResult> DeleteCapturedAsync(IReadOnlyList<MessageDelivery> targets, CancellationToken cancellationToken)
    {
        if (session is null || Volatile.Read(ref disposeStarted) != 0 || targets.Any(target => target.Identity.ConnectionGeneration != generation))
            throw new InvalidOperationException("Reconnect and select current DLQ deliveries before deleting.");
        var profile = SelectedProfile;
        long deleteGeneration = generation;
        string broker = ReplayNamespace.Fingerprint(profile.Connection);
        var deleter = new DlqDeliveryDeleter(source => createDeleteReceiver(profile.Connection, source));
        var result = await deleter.DeleteAsync(targets, cancellationToken);
        var confirmed = result.Outcomes.Where(outcome => outcome.Status == DlqDeleteStatus.Confirmed).Select(outcome => outcome.Identity).ToHashSet();
        if (generation == deleteGeneration && SelectedProfile.Id == profile.Id)
        {
            Browse.ForgetDeleted(confirmed);
            Search.ForgetDeleted(confirmed);
            Watch.ForgetDeleted(targets.Where(target => confirmed.Contains(target.Identity)).ToArray());
            Inspector.ForgetDeleted(confirmed);
            Inspector.Select(Surface.FocusedMessage?.Delivery);
        }
        bool saveFailed = await MarkReplayCleanupAsync(profile.Id, broker, targets, result);
        return new(result, saveFailed);
    }

    private async Task<bool> MarkReplayCleanupAsync(string profileId, string broker, IReadOnlyList<MessageDelivery> targets, DlqDeleteResult result)
    {
        var confirmedIds = result.Outcomes.Where(outcome => outcome.Status == DlqDeleteStatus.Confirmed).Select(outcome => outcome.Identity).ToHashSet();
        var confirmed = targets.Where(target => confirmedIds.Contains(target.Identity)).ToArray();
        await saveGate.WaitAsync();
        try
        {
            var families = preferences.ReplayFamilies.GetValueOrDefault(profileId) ?? [];
            var updated = families.Select(family => confirmed.Any(delivery => IsFamilyMember(delivery, family))
                ? family with { CleanupNamespace = broker } : family).ToArray();
            var attempts = OriginalDeletionOutcomes(profileId, broker, targets, result);
            if (updated.SequenceEqual(families) && attempts.SequenceEqual(preferences.ReplayAttempts)) return false;
            var map = preferences.ReplayFamilies.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (updated.Length > 0) map[profileId] = updated;
            // A confirmed broker mutation remains a session fact even if persisting its cleanup marker fails.
            preferences = preferences with { ReplayFamilies = map, ReplayAttempts = attempts };
            cleanupMarkersUnsaved = true;
            try { await store.SaveAsync(preferences, CancellationToken.None); cleanupMarkersUnsaved = false; return false; }
            catch (Exception) { return true; }
        }
        finally { saveGate.Release(); OnPropertyChanged(nameof(Preferences)); }
    }

    private static bool IsFamilyMember(MessageDelivery delivery, ReplayFamilyState family)
    {
        try { return ReplayLineage.BelongsTo(delivery, family); }
        catch (ArgumentException) { return false; }
    }
}
