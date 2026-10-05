using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class InvestigationWorkspace
{
    public IReadOnlyList<ReplayAttempt> CurrentReplayAttempts
    {
        get
        {
            string broker;
            try { broker = ReplayNamespace.Fingerprint(SelectedProfile.Connection); }
            catch (Exception exception) when (exception is ArgumentException or FormatException) { return []; }
            return preferences.ReplayAttempts.Where(attempt => attempt.ProfileId == SelectedProfile.Id
                && attempt.NamespaceFingerprint == broker).OrderByDescending(attempt => attempt.RequestedAtUtc).ToArray();
        }
    }

    public IReadOnlyList<ReplayAttempt> CurrentReplayHistory
    {
        get => CurrentReplayAttempts.Where(attempt => !attempt.HiddenFromHistory).ToArray();
    }

    public bool CanRemoveReplayHistory(ReplayAttempt requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (!mutationGate.Wait(0)) return false;
        try { return CanRemoveReplayHistoryCore(requested); }
        finally { mutationGate.Release(); }
    }

    public async Task<bool> RemoveReplayHistoryAsync(ReplayAttempt requested, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requested);
        cancellationToken.ThrowIfCancellationRequested();
        await StopReplayCleanupAsync();
        try { await mutationGate.WaitAsync(cancellationToken); }
        catch { StartReplayCleanup(); throw; }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedProfileId = SelectedProfile.Id;
            string broker;
            try { broker = ReplayNamespace.Fingerprint(SelectedProfile.Connection); }
            catch (Exception exception) when (exception is ArgumentException or FormatException) { return false; }

            await saveGate.WaitAsync(cancellationToken);
            try
            {
                if (SelectedProfile.Id != selectedProfileId
                    || ReplayNamespace.Fingerprint(SelectedProfile.Connection) != broker
                    || !CanRemoveReplayHistoryCore(requested, selectedProfileId, broker)) return false;
                var current = preferences.ReplayAttempts.Single(attempt => attempt.AttemptId == requested.AttemptId);
                var updated = preferences with { ReplayAttempts = preferences.ReplayAttempts
                    .Select(attempt => attempt.AttemptId == current.AttemptId
                        ? attempt with { HiddenFromHistory = true } : attempt).ToArray() };
                try { await store.SaveAsync(updated, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { return false; }
                preferences = updated;
                return true;
            }
            finally { saveGate.Release(); OnPropertyChanged(nameof(Preferences)); }
        }
        finally { mutationGate.Release(); StartReplayCleanup(); }
    }

    private bool CanRemoveReplayHistoryCore(ReplayAttempt requested, string? profileId = null, string? broker = null)
    {
        if (profileId is null)
        {
            profileId = SelectedProfile.Id;
            try { broker = ReplayNamespace.Fingerprint(SelectedProfile.Connection); }
            catch (Exception exception) when (exception is ArgumentException or FormatException) { return false; }
        }
        var current = CurrentReplayAttempts.SingleOrDefault(attempt => attempt.AttemptId == requested.AttemptId);
        return current is not null && !current.HiddenFromHistory
            && current.ProfileId == profileId && current.NamespaceFingerprint == broker
            && current.SendStatus != ReplaySendStatus.NotSent
            && current.Observation is { IsComplete: true, IsAbsent: true };
    }

    private async Task<bool> SaveReplayOutcomeAsync(ReplayAttempt attempt)
    {
        await saveGate.WaitAsync();
        try
        {
            var updated = preferences with { ReplayAttempts = preferences.ReplayAttempts
                .Select(existing => existing.AttemptId == attempt.AttemptId
                    ? attempt with { HiddenFromHistory = existing.HiddenFromHistory } : existing).ToArray() };
            try
            {
                // Outcome recording must survive cancellation of the broker operation.
                await store.SaveAsync(updated, CancellationToken.None);
                preferences = updated;
                return true;
            }
            catch (Exception)
            {
                // Keep the durable conservative state in memory too; a later unrelated save must not hide uncertainty.
                Log("The replay outcome could not be saved. History remains uncertain; check the broker before retrying.", true);
                return false;
            }
        }
        finally { saveGate.Release(); OnPropertyChanged(nameof(Preferences)); }
    }

    private ReplayAttempt[] OriginalDeletionOutcomes(string profileId, string broker,
        IReadOnlyList<MessageDelivery> targets, DlqDeleteResult result)
    {
        var outcomes = result.Outcomes.Where(outcome => outcome.Status != DlqDeleteStatus.NotAttempted)
            .ToDictionary(outcome => outcome.Identity, outcome => OriginalStatusAfterDelete(outcome.Status));
        var originals = targets.Where(target => outcomes.ContainsKey(target.Identity))
            .Select(target => (target.Identity, Fingerprint: ReplayLineage.Fingerprint(target))).ToArray();
        return preferences.ReplayAttempts.Select(attempt =>
        {
            if (attempt.ProfileId != profileId || attempt.NamespaceFingerprint != broker) return attempt;
            var original = originals.FirstOrDefault(target => target.Identity.Source == attempt.OriginalSource
                && target.Identity.SequenceNumber == attempt.OriginalSequenceNumber && target.Fingerprint == attempt.OriginalFingerprint);
            return original.Identity is null ? attempt : attempt with { OriginalStatus = outcomes[original.Identity] };
        }).ToArray();
    }

    private static ReplayOriginalStatus OriginalStatusAfterDelete(DlqDeleteStatus status) => status switch
    {
        DlqDeleteStatus.Confirmed => ReplayOriginalStatus.Deleted,
        DlqDeleteStatus.Uncertain => ReplayOriginalStatus.Uncertain,
        DlqDeleteStatus.Unavailable => ReplayOriginalStatus.Unavailable,
        _ => ReplayOriginalStatus.Retained
    };
}
