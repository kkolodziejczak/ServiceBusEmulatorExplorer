using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Agent;

public sealed record AgentArrival(long Sequence, DateTimeOffset AcceptedAtUtc, MessageDelivery Delivery);

/// <param name="Expired">The cursor predates a reset or evicted arrivals; the agent should start again from <see cref="NextCursor"/>.</param>
public sealed record AgentArrivalPage(IReadOnlyList<AgentArrival> Arrivals, string NextCursor, bool Expired, bool HasMore);

/// <summary>
/// Bounded, ordered log of Watch arrivals for agents. It is independent of the arrivals the user
/// acknowledges in the window. Cursors carry the journal and epoch, so a reset (profile switch,
/// disconnect) or eviction is reported as expired instead of silently skipping arrivals.
/// </summary>
public sealed class AgentArrivalJournal(int capacity = 500, TimeProvider? clock = null)
{
    private readonly object gate = new();
    private readonly LinkedList<AgentArrival> arrivals = new();
    private readonly string journalId = Guid.NewGuid().ToString("N")[..8];
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private long epoch;
    private long nextSequence = 1;

    public void Append(MessageDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        lock (gate)
        {
            arrivals.AddLast(new AgentArrival(nextSequence++, clock.GetUtcNow(), delivery));
            while (arrivals.Count > capacity) arrivals.RemoveFirst();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            arrivals.Clear();
            epoch++;
            nextSequence = 1;
        }
    }

    /// <summary>Reads arrivals after <paramref name="cursor"/>; a null cursor returns everything retained.</summary>
    public AgentArrivalPage Read(string? cursor, int limit)
    {
        limit = Math.Clamp(limit, 1, 100);
        lock (gate)
        {
            long after = 0;
            bool expired = false;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                if (TryParse(cursor, out long cursorEpoch, out long sequence) && cursorEpoch == epoch && sequence < nextSequence)
                {
                    after = sequence;
                    long oldest = arrivals.First?.Value.Sequence ?? nextSequence;
                    expired = sequence + 1 < oldest;
                }
                else expired = true;
            }

            var page = arrivals.Where(arrival => arrival.Sequence > after).Take(limit + 1).ToList();
            bool hasMore = page.Count > limit;
            if (hasMore) page.RemoveAt(page.Count - 1);
            long last = page.Count > 0 ? page[^1].Sequence : Math.Max(after, nextSequence - 1);
            return new AgentArrivalPage(page, $"{journalId}.{epoch}.{last}", expired, hasMore);
        }
    }

    private bool TryParse(string cursor, out long cursorEpoch, out long sequence)
    {
        cursorEpoch = sequence = 0;
        var parts = cursor.Split('.');
        return parts.Length == 3 && parts[0] == journalId
            && long.TryParse(parts[1], out cursorEpoch) && long.TryParse(parts[2], out sequence) && sequence >= 0;
    }
}
