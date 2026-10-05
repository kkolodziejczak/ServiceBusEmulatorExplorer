using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record PreparedReplayCleanup(ReplayAttempt Attempt, MessageDelivery Original);

public sealed partial class InvestigationWorkspace
{
    public Task CheckReplayStatusAsync(CancellationToken cancellationToken = default) =>
        WithReplayHistoryGateAsync(async token =>
        {
            await ObserveReplayAttemptsAsync(CurrentReplayHistory, token);
            return true;
        }, cancellationToken);

    private async Task ObserveReplayAttemptsAsync(IReadOnlyList<ReplayAttempt> attempts, CancellationToken token)
    {
        var currentSession = session ?? throw new InvalidOperationException("Connect before checking replay status.");
        long observedGeneration = generation;
        var ids = attempts.Where(attempt => attempt.SendStatus != ReplaySendStatus.NotSent)
            .Select(attempt => attempt.Reservation.MessageId).Distinct(StringComparer.Ordinal).ToArray();
        var observation = await new ReplayObserver(currentSession.Browser, currentSession.Messages)
            .ObserveAsync(ids, observedGeneration, preferences.SearchDeliveryBudget,
                TimeSpan.FromSeconds(preferences.SearchTimeBudgetSeconds), token);
        if (generation != observedGeneration || !ReferenceEquals(session, currentSession))
            throw new OperationCanceledException("The connection changed during the status check.");
        var attemptIds = attempts.Select(attempt => attempt.AttemptId).ToHashSet();
        await SaveReplayHistoryAsync(existing => attemptIds.Contains(existing.AttemptId)
            && observation.Observations.TryGetValue(existing.Reservation.MessageId, out var result)
                ? existing with { Observation = result } : existing, CancellationToken.None);
    }

    public Task<MessageDelivery?> FindReplayOriginalAsync(ReplayAttempt attempt, CancellationToken cancellationToken = default) =>
        WithReplayHistoryGateAsync(token => FindReplayOriginalCoreAsync(RequireReplayAttempt(attempt), token), cancellationToken);

    private ReplayAttempt RequireReplayAttempt(ReplayAttempt attempt) => CurrentReplayHistory
        .SingleOrDefault(existing => existing.AttemptId == attempt.AttemptId)
        ?? throw new InvalidOperationException("This replay belongs to another connection or is no longer saved.");

    private async Task<MessageDelivery?> FindReplayOriginalCoreAsync(ReplayAttempt attempt, CancellationToken token)
    {
        var currentSession = session ?? throw new InvalidOperationException("Connect before finding the original.");
        long lookupGeneration = generation;
        var messages = await currentSession.Messages.PeekMessagesAsync(attempt.OriginalSource, MessageBucket.DeadLetter,
            1, attempt.OriginalSequenceNumber, token);
        token.ThrowIfCancellationRequested();
        if (generation != lookupGeneration || !ReferenceEquals(session, currentSession)) throw new OperationCanceledException();
        var original = messages.FirstOrDefault(message => message.SequenceNumber == attempt.OriginalSequenceNumber);
        if (original is null) return null;
        var delivery = new MessageDelivery(new(lookupGeneration, attempt.OriginalSource,
            MessageBucket.DeadLetter, original.SequenceNumber), original);
        return ReplayLineage.Fingerprint(delivery) == attempt.OriginalFingerprint ? delivery : null;
    }

    private async Task<T> WithReplayHistoryGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopReplayCleanupAsync();
        try { await mutationGate.WaitAsync(cancellationToken); }
        catch { StartReplayCleanup(); throw; }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(TimeSpan.FromSeconds(preferences.SearchTimeBudgetSeconds + 30));
        mutationCancellation = operation;
        try
        {
            if (session is null || Volatile.Read(ref disposeStarted) != 0) throw new InvalidOperationException("Connect before using replay history actions.");
            return await action(operation.Token);
        }
        finally { mutationCancellation = null; mutationGate.Release(); StartReplayCleanup(); }
    }

    private async Task SaveReplayHistoryAsync(Func<ReplayAttempt, ReplayAttempt> update, CancellationToken token,
        bool preserveSessionFactOnFailure = false)
    {
        await saveGate.WaitAsync(token);
        try
        {
            var updated = preferences with { ReplayAttempts = preferences.ReplayAttempts.Select(update).ToArray() };
            try { await store.SaveAsync(updated, token); }
            catch when (preserveSessionFactOnFailure)
            {
                preferences = updated;
                Log("The original-message outcome could not be saved. Verify the original after reopening history.", true);
                return;
            }
            preferences = updated;
        }
        finally { saveGate.Release(); OnPropertyChanged(nameof(Preferences)); }
    }
}
