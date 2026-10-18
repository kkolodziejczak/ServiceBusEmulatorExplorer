using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// Counts every delivery of an entity bucket by peeking to its end, so the tree shows totals
/// rather than the loaded page. Targets are counted one at a time; the newest request goes first.
/// </summary>
internal sealed class ObservedCountScanner(
    IServiceBusMessageService messages,
    long connectionGeneration,
    Action<WatchTarget, ObservedDeliveryCount> publish,
    Action<WatchTarget, Exception> fail)
{
    private const int PeekSize = 100;
    private readonly List<WatchTarget> pending = [];
    private readonly CancellationTokenSource stop = new();

    public Task Idle { get; private set; } = Task.CompletedTask;

    public void Request(IReadOnlyCollection<WatchTarget> targets)
    {
        if (targets.Count == 0 || stop.IsCancellationRequested) return;
        pending.RemoveAll(targets.Contains);
        pending.InsertRange(0, targets);
        if (Idle.IsCompleted) Idle = RunAsync();
    }

    public void Stop() => stop.Cancel();

    private async Task RunAsync()
    {
        while (pending.Count > 0 && !stop.IsCancellationRequested)
        {
            var target = pending[0];
            pending.RemoveAt(0);
            try
            {
                var count = await CountAsync(target);
                if (!stop.IsCancellationRequested) publish(target, count);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error) { fail(target, error); }
        }
    }

    private async Task<ObservedDeliveryCount> CountAsync(WatchTarget target)
    {
        var deliveries = new HashSet<DeliveryIdentity>();
        long? from = null;
        while (true)
        {
            var batch = await messages.PeekMessagesAsync(target.Address, target.Bucket, PeekSize, from, stop.Token);
            if (batch.Count == 0) break;
            foreach (var message in batch)
                deliveries.Add(new(connectionGeneration, target.Address, target.Bucket, message.SequenceNumber));
            long last = batch.Max(message => message.SequenceNumber);
            if (last == long.MaxValue) break;
            from = last + 1;
        }
        return new(deliveries, DateTimeOffset.UtcNow);
    }
}
