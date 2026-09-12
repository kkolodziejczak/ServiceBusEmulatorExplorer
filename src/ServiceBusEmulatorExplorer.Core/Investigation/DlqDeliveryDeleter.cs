using Azure.Messaging.ServiceBus;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public enum DlqDeleteStatus { Confirmed, Unavailable, Uncertain, NotAttempted }
public sealed record DlqDeleteOutcome(DeliveryIdentity Identity, DlqDeleteStatus Status);
public sealed record DlqDeleteResult(IReadOnlyList<DlqDeleteOutcome> Outcomes, bool ScanLimitReached, bool CleanupIncomplete);

public interface IDlqDeleteReceiver : IAsyncDisposable
{
    Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAsync(int take, CancellationToken cancellationToken);
    Task CompleteAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken);
    Task AbandonAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken);
}

/// <summary>Targets only captured DLQ deliveries; all non-target locks are released after each bounded scan.</summary>
public sealed class DlqDeliveryDeleter
{
    private readonly Func<EntityAddress, IDlqDeleteReceiver> createReceiver;
    private readonly int maxScannedDeliveries;

    public DlqDeliveryDeleter(Func<EntityAddress, IDlqDeleteReceiver> createReceiver, int maxScannedDeliveries = 10_000)
    {
        this.createReceiver = createReceiver ?? throw new ArgumentNullException(nameof(createReceiver));
        if (maxScannedDeliveries < 1) throw new ArgumentOutOfRangeException(nameof(maxScannedDeliveries));
        this.maxScannedDeliveries = maxScannedDeliveries;
    }

    public async Task<DlqDeleteResult> DeleteAsync(IReadOnlyList<MessageDelivery> targets, CancellationToken cancellationToken)
    {
        ValidateTargets(targets);
        var outcomes = targets.ToDictionary(target => target.Identity, _ => DlqDeleteStatus.NotAttempted);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(TimeSpan.FromSeconds(30));
        int scanned = 0;
        bool cleanupIncomplete = false;
        foreach (var group in targets.GroupBy(target => target.Identity.Source))
        {
            if (operation.IsCancellationRequested) break;
            var remaining = group.ToDictionary(target => target.Identity.SequenceNumber);
            var held = new List<ServiceBusReceivedMessage>();
            IDlqDeleteReceiver? receiver = null;
            try
            {
                if (scanned >= maxScannedDeliveries) continue;
                receiver = createReceiver(group.Key);
                while (remaining.Count > 0 && scanned < maxScannedDeliveries)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var batch = await receiver.ReceiveAsync(Math.Min(100, maxScannedDeliveries - scanned), operation.Token).ConfigureAwait(false);
                    // Own every returned lock before cancellation or projection can fail.
                    held.AddRange(batch);
                    scanned += batch.Count;
                    if (batch.Count == 0) break;
                    foreach (var message in batch)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        if (!remaining.TryGetValue(message.SequenceNumber, out var target)) continue;
                        var observed = new MessageDelivery(target.Identity, MessageProjection.Create(message));
                        if (ReplayLineage.Fingerprint(target) != ReplayLineage.Fingerprint(observed)) continue;
                        remaining.Remove(message.SequenceNumber);
                        try
                        {
                            await receiver.CompleteAsync(message, operation.Token).ConfigureAwait(false);
                            outcomes[target.Identity] = DlqDeleteStatus.Confirmed;
                            held.Remove(message);
                        }
                        catch (Exception)
                        {
                            // The broker may have completed the message before its response was lost.
                            outcomes[target.Identity] = DlqDeleteStatus.Uncertain;
                        }
                    }
                }
            }
            catch (Exception) { /* Per-delivery outcomes below retain all completed settlements. */ }
            finally
            {
                foreach (var target in remaining.Values)
                    outcomes[target.Identity] = operation.IsCancellationRequested ? DlqDeleteStatus.NotAttempted : DlqDeleteStatus.Unavailable;
                if (receiver is not null)
                    cleanupIncomplete |= !await ReleaseAsync(receiver, held).ConfigureAwait(false);
            }
        }
        return new(targets.Select(target => new DlqDeleteOutcome(target.Identity, outcomes[target.Identity])).ToArray(),
            scanned >= maxScannedDeliveries && outcomes.Values.Any(status => status is DlqDeleteStatus.Unavailable or DlqDeleteStatus.NotAttempted), cleanupIncomplete);
    }

    private static void ValidateTargets(IReadOnlyList<MessageDelivery> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Any(target => target is null || target.Identity.Bucket != MessageBucket.DeadLetter)
            || targets.Select(target => target.Identity.ConnectionGeneration).Distinct().Count() > 1
            || targets.Select(target => target.Identity).Distinct().Count() != targets.Count)
            throw new ArgumentException("Delete requires distinct DLQ deliveries from one connection generation.", nameof(targets));
        foreach (var target in targets) _ = ReplayLineage.Destination(target.Identity.Source);
    }

    private static async Task<bool> ReleaseAsync(IDlqDeleteReceiver receiver, IReadOnlyList<ServiceBusReceivedMessage> held)
    {
        bool complete = true;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var message in held)
        {
            if (cleanup.IsCancellationRequested) { complete = false; break; }
            try { await receiver.AbandonAsync(message, cleanup.Token).ConfigureAwait(false); }
            catch (Exception) { complete = false; }
        }
        try { await receiver.DisposeAsync().AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false); }
        catch (Exception) { complete = false; }
        return complete;
    }
}
