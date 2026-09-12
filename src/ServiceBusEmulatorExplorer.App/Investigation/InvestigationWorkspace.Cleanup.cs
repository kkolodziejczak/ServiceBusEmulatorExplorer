using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class InvestigationWorkspace
{
    private CancellationTokenSource? cleanupCancellation;
    private Task? cleanupTask;
    private readonly Dictionary<Guid, (int Deliveries, int Seconds)> cleanupBudgets = [];
    private int cleanupNextFamily;
    private bool cleanupMarkersUnsaved;

    private void StartReplayCleanup()
    {
        if (session is null || Volatile.Read(ref disposeStarted) != 0 || cleanupTask is { IsCompleted: false }
            || !preferences.ReplayFamilies.TryGetValue(SelectedProfile.Id, out var families) || !families.Any(family => family.CleanupNamespace is not null)) return;
        string broker;
        try { broker = ReplayNamespace.Fingerprint(SelectedProfile.Connection); }
        catch (ArgumentException) { return; }
        if (!families.Any(family => string.Equals(family.CleanupNamespace, broker, StringComparison.OrdinalIgnoreCase))) return;
        cleanupCancellation?.Dispose();
        cleanupCancellation = new();
        cleanupTask = RunReplayCleanupAsync(SelectedProfile.Id, generation, broker, cleanupCancellation.Token);
    }

    private async Task RunReplayCleanupAsync(string profileId, long cleanupGeneration, string broker, CancellationToken cancellationToken)
    {
        bool reportedPending = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested && generation == cleanupGeneration && SelectedProfile.Id == profileId)
            {
                try
                {
                    int removed = await CleanupReplayFamiliesAsync(cancellationToken);
                    if (generation != cleanupGeneration || cancellationToken.IsCancellationRequested) return;
                    if (removed > 0) { Log($"Removed {removed} unused replay counter(s) after complete DLQ checks."); continue; }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && generation == cleanupGeneration) { }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    if (!reportedPending && generation == cleanupGeneration)
                    {
                        Log("Replay counter cleanup is pending; saved counters are retained until checks and persistence succeed.", true);
                        reportedPending = true;
                    }
                }
                if (!preferences.ReplayFamilies.TryGetValue(profileId, out var families) || !families.Any(family => string.Equals(family.CleanupNamespace, broker, StringComparison.OrdinalIgnoreCase))) return;
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<int> CleanupReplayFamiliesAsync(CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(TimeSpan.FromSeconds(30));
        mutationCancellation = operation;
        try
        {
            if (session is null || Volatile.Read(ref disposeStarted) != 0) return 0;
            var currentSession = session;
            long cleanupGeneration = generation;
            string profileId = SelectedProfile.Id;
            string broker = ReplayNamespace.Fingerprint(SelectedProfile.Connection);
            if (!preferences.ReplayFamilies.TryGetValue(profileId, out var saved)) return 0;
            var candidates = saved.Where(family => string.Equals(family.CleanupNamespace, broker, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) return 0;
            foreach (var family in candidates) cleanupBudgets.TryAdd(family.FamilyId, (10_000, 30));
            operation.CancelAfter(TimeSpan.FromSeconds(candidates.Max(family => cleanupBudgets[family.FamilyId].Seconds)));
            if (cleanupMarkersUnsaved)
            {
                await saveGate.WaitAsync(operation.Token);
                try
                {
                    await store.SaveAsync(preferences, operation.Token);
                    cleanupMarkersUnsaved = false;
                }
                finally { saveGate.Release(); }
            }
            EntityDiscoverySnapshot discovery;
            try
            {
                discovery = await currentSession.Browser.DiscoverAsync(operation.Token);
                operation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                foreach (var family in candidates)
                {
                    var budget = cleanupBudgets[family.FamilyId];
                    cleanupBudgets[family.FamilyId] = (budget.Deliveries, Math.Min(600, budget.Seconds * 2));
                }
                throw;
            }
            if (generation != cleanupGeneration || !ReferenceEquals(session, currentSession)) return 0;
            var scanner = new ReplayFamilyScanner(currentSession.Messages);
            var absent = new List<ReplayFamilyState>();
            int remaining = candidates.Max(family => cleanupBudgets[family.FamilyId].Deliveries);
            int start = cleanupNextFamily % candidates.Length;
            for (int offset = 0; offset < candidates.Length; offset++)
            {
                if (remaining == 0) break;
                int index = (start + offset) % candidates.Length;
                var family = candidates[index];
                var budget = cleanupBudgets[family.FamilyId];
                var result = await scanner.ScanAsync(family, discovery, cleanupGeneration, Math.Min(remaining, budget.Deliveries),
                    operation.Token, TimeSpan.FromSeconds(budget.Seconds));
                if (result.LimitReached || (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested))
                    cleanupBudgets[family.FamilyId] = ((int)Math.Min(int.MaxValue, (long)budget.Deliveries * 2), Math.Min(600, budget.Seconds * 2));
                cleanupNextFamily = (index + 1) % candidates.Length;
                remaining -= result.ScannedDeliveries;
                if (result.Presence == ReplayFamilyPresence.Absent) { absent.Add(family); break; }
                operation.Token.ThrowIfCancellationRequested();
            }
            operation.Token.ThrowIfCancellationRequested();
            if (absent.Count == 0 || generation != cleanupGeneration || !ReferenceEquals(session, currentSession)) return 0;
            await saveGate.WaitAsync(operation.Token);
            try
            {
                if (generation != cleanupGeneration || SelectedProfile.Id != profileId || !ReferenceEquals(session, currentSession)) return 0;
                var map = preferences.ReplayFamilies.ToDictionary(pair => pair.Key, pair => pair.Value);
                if (!map.TryGetValue(profileId, out var latest)) return 0;
                var retained = latest.Where(family => !absent.Contains(family)).ToArray();
                int removed = latest.Count - retained.Length;
                if (removed == 0) return 0;
                if (retained.Length == 0) map.Remove(profileId); else map[profileId] = retained;
                await store.SaveAsync(preferences with { ReplayFamilies = map }, operation.Token);
                cleanupMarkersUnsaved = false;
                preferences = preferences with { ReplayFamilies = map };
                foreach (var family in absent) cleanupBudgets.Remove(family.FamilyId);
                return removed;
            }
            finally { saveGate.Release(); }
        }
        finally { mutationCancellation = null; mutationGate.Release(); }
    }

    private async Task StopReplayCleanupAsync()
    {
        var cancellation = cleanupCancellation;
        var task = cleanupTask;
        cancellation?.Cancel();
        if (task is not null) await task;
        if (ReferenceEquals(cleanupTask, task))
        {
            cleanupTask = null;
            if (ReferenceEquals(cleanupCancellation, cancellation)) cleanupCancellation = null;
            cleanupBudgets.Clear();
            cleanupNextFamily = 0;
        }
        cancellation?.Dispose();
    }
}
