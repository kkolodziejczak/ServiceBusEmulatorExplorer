using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed record ReplayCopyOutcome(ReplayReservation Reservation, ReplaySendStatus Status, bool HistoryPersistenceFailed = false);

public sealed partial class InvestigationWorkspace
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private CancellationTokenSource? mutationCancellation;

    public PreparedReplay PrepareReplay(MessageDelivery delivery, string? editedBody = null) =>
        PrepareReplays([delivery], editedBody).Single();

    public IReadOnlyList<PreparedReplay> PrepareReplays(IReadOnlyList<MessageDelivery> deliveries, string? editedBody = null)
    {
        if (deliveries.Count > 1 && editedBody is not null)
            throw new ArgumentException("A body draft applies to one message only.");
        var families = preferences.ReplayFamilies.TryGetValue(SelectedProfile.Id, out var saved) ? saved.ToList() : [];
        var result = new List<PreparedReplay>();
        foreach (var delivery in deliveries)
        {
            RequireCurrentReplay(delivery);
            var reservation = ReplayLineage.Reserve(delivery, families);
            // Freeze payload bytes and metadata at review time, rather than rereading an editor on send.
            var original = delivery.Message;
            var snapshot = original with
            {
                RawBody = new BinaryData((original.RawBody ?? BinaryData.FromString(original.Body)).ToArray()),
                SystemProperties = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(original.SystemProperties.ToDictionary()),
                ApplicationProperties = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(original.ApplicationProperties.ToDictionary())
            };
            result.Add(new(SelectedProfile.Id, ReplayNamespace.Fingerprint(SelectedProfile.Connection),
                new(delivery.Identity, snapshot), reservation, editedBody));
            families.RemoveAll(family => family.FamilyId == reservation.Family.FamilyId);
            families.Add(reservation.Family);
        }
        return result;
    }

    private void RequireCurrentReplay(MessageDelivery delivery)
    {
        if (session is null || generation != delivery.Identity.ConnectionGeneration || Volatile.Read(ref disposeStarted) != 0)
            throw new InvalidOperationException("Reconnect and select a current DLQ delivery before replaying.");
    }

    public Task<ReplayCopyOutcome> ReplayAsync(MessageDelivery delivery, string? editedBody = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = PrepareReplay(delivery, editedBody);
        return SendReplayAsync(prepared, prepared.Reservation.MessageId, cancellationToken);
    }

    public async Task<ReplayCopyOutcome> SendReplayAsync(PreparedReplay prepared, string reviewedMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (ReplayAttempt.MessageIdProblem(reviewedMessageId) is { } problem) throw new ArgumentException(problem);
        cancellationToken.ThrowIfCancellationRequested();
        await StopReplayCleanupAsync();
        try { await mutationGate.WaitAsync(cancellationToken); }
        catch { StartReplayCleanup(); throw; }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(TimeSpan.FromSeconds(30));
        mutationCancellation = operation;
        try
        {
            var delivery = prepared.Delivery;
            RequireCurrentReplay(delivery);
            var profile = SelectedProfile;
            long replayGeneration = generation;
            var reservation = prepared.Reservation with { MessageId = reviewedMessageId };
            Azure.Messaging.ServiceBus.ServiceBusMessage message;
            ReplayAttempt attempt;
            await saveGate.WaitAsync(operation.Token);
            try
            {
                if (session is null || generation != replayGeneration) throw new OperationCanceledException();
                var families = preferences.ReplayFamilies.TryGetValue(profile.Id, out var saved) ? saved : [];
                if (profile.Id != prepared.ProfileId || ReplayNamespace.Fingerprint(profile.Connection) != prepared.NamespaceFingerprint
                    || ReplayLineage.Fingerprint(delivery) != prepared.OriginalFingerprint
                    || preferences.ReplayAttempts.Any(item => item.AttemptId == prepared.AttemptId)
                    || ReplayLineage.Reserve(delivery, families).Family != reservation.Family)
                    throw new InvalidOperationException("This replay review is no longer current. Review the message again.");
                message = ReplayLineage.CreateMessage(delivery, reservation, prepared.EditedBody);
                var updatedFamilies = preferences.ReplayFamilies.ToDictionary(pair => pair.Key, pair => pair.Value);
                updatedFamilies[profile.Id] = families.Where(family => family.FamilyId != reservation.Family.FamilyId)
                    .Append(reservation.Family).ToArray();
                attempt = new(prepared.AttemptId, profile.Id, prepared.NamespaceFingerprint, reservation,
                    delivery.Identity.Source, delivery.Identity.SequenceNumber, prepared.OriginalFingerprint,
                    delivery.Message.MessageId, DateTimeOffset.UtcNow, ReplaySendStatus.Uncertain);
                var updated = preferences with { ReplayFamilies = updatedFamilies,
                    ReplayAttempts = preferences.ReplayAttempts.Append(attempt).ToArray() };
                await store.SaveAsync(updated, operation.Token);
                preferences = updated;
            }
            finally { saveGate.Release(); }
            OnPropertyChanged(nameof(Preferences));
            IReplayCopySender? sender = null;
            bool sendStarted = false;
            var status = ReplaySendStatus.NotSent;
            var sendFailure = ReplaySendFailure.Cancelled;
            try
            {
                if (!operation.IsCancellationRequested && session is not null && generation == replayGeneration)
                {
                    sender = createReplaySender(profile.Connection);
                    if (!operation.IsCancellationRequested && session is not null && generation == replayGeneration)
                    {
                        sendStarted = true;
                        await sender.SendAsync(ReplayLineage.Destination(delivery.Identity.Source), message, operation.Token);
                        status = ReplaySendStatus.Confirmed;
                        sendFailure = ReplaySendFailure.None;
                    }
                }
            }
            catch (Exception exception)
            {
                status = sendStarted ? ReplaySendStatus.Uncertain : ReplaySendStatus.NotSent;
                sendFailure = ReplaySendDiagnostics.Classify(exception);
            }
            finally
            {
                if (sender is not null)
                {
                    try { await sender.DisposeAsync(); }
                    catch (Exception) { }
                }
            }
            bool persisted = await SaveReplayOutcomeAsync(attempt with { SendStatus = status, SendFailure = sendFailure,
                SentAtUtc = status == ReplaySendStatus.Confirmed ? DateTimeOffset.UtcNow : null });
            return new(reservation, status, !persisted);
        }
        finally { mutationCancellation = null; mutationGate.Release(); StartReplayCleanup(); }
    }
}
