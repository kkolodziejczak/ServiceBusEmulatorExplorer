using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class InvestigationWorkspace
{
    public bool CanReviewReplayOriginalCleanup(ReplayAttempt requested)
    {
        var attempt = CurrentReplayHistory.SingleOrDefault(item => item.AttemptId == requested.AttemptId);
        return attempt is not null && attempt.SendStatus == ReplaySendStatus.Confirmed
            && attempt.OriginalStatus != ReplayOriginalStatus.Deleted
            && RelatedReplayAttempts(attempt).All(item => item.SendStatus == ReplaySendStatus.NotSent
                || (item.SendStatus == ReplaySendStatus.Confirmed && item.Observation?.IsAbsent == true));
    }

    public Task<PreparedReplayCleanup?> PrepareReplayCleanupAsync(ReplayAttempt attempt, CancellationToken cancellationToken = default) =>
        WithReplayHistoryGateAsync(token => PrepareReplayCleanupCoreAsync(attempt, token), cancellationToken);

    private async Task<PreparedReplayCleanup?> PrepareReplayCleanupCoreAsync(ReplayAttempt requested, CancellationToken token)
    {
        var attempt = RequireReplayAttempt(requested);
        var related = RelatedReplayAttempts(attempt);
        if (attempt.OriginalStatus == ReplayOriginalStatus.Deleted)
        {
            Log("The original was already deleted by an earlier history action.");
            return null;
        }
        if (attempt.SendStatus != ReplaySendStatus.Confirmed || related.Any(item => item.SendStatus == ReplaySendStatus.Uncertain))
        {
            Log("Original cleanup is unavailable while a related replay send is uncertain or unconfirmed.", true);
            return null;
        }
        await ObserveReplayAttemptsAsync(related, token);
        token.ThrowIfCancellationRequested();
        if (RelatedReplayAttempts(attempt).Any(item => item.SendStatus != ReplaySendStatus.NotSent && item.Observation?.IsAbsent != true))
        {
            Log("Keep the original: a replay is still present, or the Active/DLQ scan is incomplete.", true);
            return null;
        }
        var original = await FindReplayOriginalCoreAsync(attempt, token);
        if (original is null)
        {
            await SaveOriginalStatusAsync(attempt, ReplayOriginalStatus.Unavailable);
            Log("The original is unavailable or already absent. No deletion was performed.");
            return null;
        }
        return new(RequireReplayAttempt(attempt), original);
    }

    private ReplayAttempt[] RelatedReplayAttempts(ReplayAttempt attempt) => CurrentReplayAttempts
        .Where(item => item.OriginalFingerprint == attempt.OriginalFingerprint
            && item.OriginalSource == attempt.OriginalSource && item.OriginalSequenceNumber == attempt.OriginalSequenceNumber).ToArray();

    public Task DeleteReplayOriginalAsync(PreparedReplayCleanup prepared, CancellationToken cancellationToken = default) =>
        WithReplayHistoryGateAsync(async token =>
        {
            if (prepared.Original.Identity.ConnectionGeneration != generation)
                throw new InvalidOperationException("The connection changed. Review deletion again.");
            var current = await PrepareReplayCleanupCoreAsync(prepared.Attempt, token);
            if (current is null) return false;
            if (current.Original.Identity != prepared.Original.Identity
                || ReplayLineage.Fingerprint(current.Original) != ReplayLineage.Fingerprint(prepared.Original))
                throw new InvalidOperationException("The original changed. Review deletion again.");
            var result = await DeleteCapturedAsync([current.Original], token);
            var outcome = result.Deletion.Outcomes.Single();
            var status = OriginalStatusAfterDelete(outcome.Status);
            if (result.CleanupPersistenceFailed)
                Log("The original-message outcome could not be saved. Verify the original after reopening history.", true);
            Log(status switch
            {
                ReplayOriginalStatus.Deleted => "Original DLQ message deleted after complete replay checks and explicit confirmation.",
                ReplayOriginalStatus.Uncertain => "Original deletion is uncertain. Check the DLQ before retrying.",
                ReplayOriginalStatus.Unavailable => "The original could not be acquired and verified. No deletion is confirmed.",
                _ => "Original deletion was not attempted."
            }, status != ReplayOriginalStatus.Deleted);
            return status == ReplayOriginalStatus.Deleted;
        }, cancellationToken);

    private Task SaveOriginalStatusAsync(ReplayAttempt original, ReplayOriginalStatus status) =>
        SaveReplayHistoryAsync(item => item.ProfileId == original.ProfileId
            && item.NamespaceFingerprint == original.NamespaceFingerprint
            && item.OriginalFingerprint == original.OriginalFingerprint
                ? item with { OriginalStatus = status } : item,
            CancellationToken.None, preserveSessionFactOnFailure: true);
}
