using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

public enum ReplayFamilyPresence { Absent, Present, Incomplete }
public sealed record ReplayFamilyScanResult(ReplayFamilyPresence Presence, int ScannedDeliveries, bool LimitReached = false);

/// <summary>Non-consuming family absence evidence; only complete source discovery and scans can prove absence.</summary>
public sealed class ReplayFamilyScanner(IServiceBusMessageService messages)
{
    public async Task<ReplayFamilyScanResult> ScanAsync(ReplayFamilyState family, EntityDiscoverySnapshot discovery,
        long generation, int maxScannedDeliveries, CancellationToken cancellationToken, TimeSpan? timeBudget = null)
    {
        ReplayLineage.ValidateFamily(family);
        ArgumentNullException.ThrowIfNull(discovery);
        if (maxScannedDeliveries < 1) throw new ArgumentOutOfRangeException(nameof(maxScannedDeliveries));
        var sources = RelevantSources(family, discovery);
        if (sources is null) return new(ReplayFamilyPresence.Incomplete, 0);
        TimeSpan duration = timeBudget ?? TimeSpan.FromSeconds(30);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(timeBudget));
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(duration);
        int scanned = 0;
        try
        {
            foreach (var source in sources)
            {
                long nextSequence = 0;
                while (true)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (scanned >= maxScannedDeliveries) return new(ReplayFamilyPresence.Incomplete, scanned, true);
                    int take = Math.Min(100, maxScannedDeliveries - scanned);
                    var page = await messages.PeekMessagesAsync(source, MessageBucket.DeadLetter, take, nextSequence, operation.Token).ConfigureAwait(false);
                    operation.Token.ThrowIfCancellationRequested();
                    if (page.Count > take || page.Any(message => message.SequenceNumber < nextSequence))
                        return new(ReplayFamilyPresence.Incomplete, scanned);
                    if (page.Count == 0) break;
                    scanned += page.Count;
                    foreach (var message in page)
                    {
                        var delivery = new MessageDelivery(new(generation, source, MessageBucket.DeadLetter, message.SequenceNumber), message);
                        if (ReplayLineage.BelongsTo(delivery, family)) return new(ReplayFamilyPresence.Present, scanned);
                    }
                    nextSequence = checked(page.Max(message => message.SequenceNumber) + 1);
                }
            }
            operation.Token.ThrowIfCancellationRequested();
            return new(ReplayFamilyPresence.Absent, scanned);
        }
        catch (Exception) { return new(ReplayFamilyPresence.Incomplete, scanned, operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested); }
    }

    private static IReadOnlyList<EntityAddress>? RelevantSources(ReplayFamilyState family, EntityDiscoverySnapshot discovery)
    {
        if (!discovery.IsComplete || discovery.Issues.Count != 0) return null;
        var addresses = discovery.Entities.Select(observation => new EntityAddress(observation.Entity.Kind,
            observation.Entity.Name, observation.Entity.TopicName)).Distinct().ToArray();
        if (!addresses.Contains(family.OriginalSource)) return null;
        if (family.OriginalSource.Kind == EntityKind.Queue) return [family.OriginalSource];
        string topic = family.OriginalSource.TopicName!;
        if (!addresses.Contains(new(EntityKind.Topic, topic))) return null;
        return addresses.Where(address => address.Kind == EntityKind.Subscription && address.TopicName == topic)
            .OrderBy(address => address.Name, StringComparer.Ordinal).ToArray();
    }
}
