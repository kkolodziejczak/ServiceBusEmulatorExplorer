using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public enum ReplaySendStatus { Confirmed, Uncertain, NotSent }
public sealed record ReplayCopyOutcome(ReplayReservation Reservation, ReplaySendStatus Status);

public sealed partial class InvestigationWorkspace
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private CancellationTokenSource? mutationCancellation;

    public async Task<ReplayCopyOutcome> ReplayAsync(MessageDelivery delivery, string? editedBody = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopReplayCleanupAsync();
        try { await mutationGate.WaitAsync(cancellationToken); }
        catch { StartReplayCleanup(); throw; }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(TimeSpan.FromSeconds(30));
        mutationCancellation = operation;
        try
        {
            if (session is null || generation != delivery.Identity.ConnectionGeneration || Volatile.Read(ref disposeStarted) != 0)
                throw new InvalidOperationException("Reconnect and select a current DLQ delivery before replaying.");
            var profile = SelectedProfile;
            long replayGeneration = generation;
            ReplayReservation reservation;
            Azure.Messaging.ServiceBus.ServiceBusMessage message;
            await saveGate.WaitAsync(operation.Token);
            try
            {
                if (session is null || generation != replayGeneration) throw new OperationCanceledException();
                var families = preferences.ReplayFamilies.TryGetValue(profile.Id, out var saved) ? saved : [];
                reservation = ReplayLineage.Reserve(delivery, families);
                message = ReplayLineage.CreateMessage(delivery, reservation, editedBody);
                var updatedFamilies = preferences.ReplayFamilies.ToDictionary(pair => pair.Key, pair => pair.Value);
                updatedFamilies[profile.Id] = families.Where(family => family.FamilyId != reservation.Family.FamilyId)
                    .Append(reservation.Family).ToArray();
                await store.SaveAsync(preferences with { ReplayFamilies = updatedFamilies }, operation.Token);
                preferences = preferences with { ReplayFamilies = updatedFamilies };
            }
            finally { saveGate.Release(); }

            if (operation.IsCancellationRequested || session is null || generation != replayGeneration)
                return new(reservation, ReplaySendStatus.NotSent);
            IReplayCopySender? sender = null;
            bool sendStarted = false;
            try
            {
                sender = createReplaySender(profile.Connection);
                if (operation.IsCancellationRequested || session is null || generation != replayGeneration)
                    return new(reservation, ReplaySendStatus.NotSent);
                sendStarted = true;
                await sender.SendAsync(ReplayLineage.Destination(delivery.Identity.Source), message, operation.Token);
                return new(reservation, ReplaySendStatus.Confirmed);
            }
            catch (Exception) { return new(reservation, sendStarted ? ReplaySendStatus.Uncertain : ReplaySendStatus.NotSent); }
            finally
            {
                if (sender is not null)
                {
                    try { await sender.DisposeAsync(); }
                    catch (Exception) { }
                }
            }
        }
        finally { mutationCancellation = null; mutationGate.Release(); StartReplayCleanup(); }
    }
}
