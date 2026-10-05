using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>Fresh, bounded, non-consuming namespace observations for exact replay IDs.</summary>
public sealed class ReplayObserver(IInvestigationEntityBrowser browser, IServiceBusMessageService messages)
{
    public async Task<ReplayObservationBatch> ObserveAsync(IReadOnlyList<string> ids, long generation,
        int maxDeliveries, TimeSpan timeBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (maxDeliveries < 1 || timeBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxDeliveries));
        string[] exactIds = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (exactIds.Length == 0) return new(new Dictionary<string, ReplayObservation>());
        string queryText = string.Join(" OR ", exactIds.Select(id => "message:" + MessageSearchQuery.QuoteLiteral(id)));
        if (!MessageSearchQuery.TryParse(queryText, out var query, out var problem)) throw new ArgumentException(problem);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeBudget);
        int sourceCount = 0;
        int scanned = 0;
        bool complete = false;
        string? limitation = null;
        IReadOnlyList<MessageDelivery> matches = [];
        try
        {
            var snapshot = await browser.DiscoverAsync(deadline.Token);
            var sources = snapshot.Entities.Where(entity => entity.Entity.Kind != EntityKind.Topic)
                .Select(entity => new EntityAddress(entity.Entity.Kind, entity.Entity.Name, entity.Entity.TopicName)).Distinct().ToArray();
            sourceCount = sources.Length;
            var search = new DeliverySearch(messages);
            search.Reset(generation, sources, query!, defaultMessageId: true);
            var result = await search.ScanNextAsync(maxDeliveries, timeBudget, deadline.Token);
            scanned = result.ScannedDeliveries;
            matches = result.Matches;
            complete = snapshot.IsComplete && snapshot.Issues.Count == 0 && result.IsComplete && !deadline.IsCancellationRequested;
            if (!complete) limitation = !snapshot.IsComplete || snapshot.Issues.Count > 0
                ? "Namespace discovery was incomplete." : $"Scan incomplete: {result.StopReason}.";
        }
        catch (OperationCanceledException) { limitation = "The scan was canceled or reached its time limit."; }
        catch (Exception) { limitation = "The namespace or a receiving source was unavailable."; }

        var checkedAt = DateTimeOffset.UtcNow;
        return new(exactIds.ToDictionary(id => id, id => new ReplayObservation(checkedAt, complete, sourceCount, scanned,
            matches.Where(delivery => delivery.Message.MessageId == id)
                .Select(delivery => new ReplayLocation(delivery.Identity.Source, delivery.Identity.Bucket,
                    delivery.Identity.Bucket == MessageBucket.DeadLetter ? "Dead letter" : delivery.Message.BrokerState?.ToString() ?? "Main queue"))
                .Distinct().ToArray(), limitation), StringComparer.Ordinal));
    }
}
