using System.Collections.Specialized;
using System.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class InvestigationWorkspace
{
    private void ReplayRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) UpdateReplayLabels(e.NewItems.Cast<MessageRow>());
    }

    private void ReplayRowPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Preferences) or nameof(SelectedProfile))
            UpdateReplayLabels(Browse.Messages.Concat(Search.Messages));
    }

    private void UpdateReplayLabels(IEnumerable<MessageRow> rows)
    {
        var attempts = CurrentReplayAttempts.Where(attempt => attempt.SendStatus != ReplaySendStatus.NotSent)
            .GroupBy(attempt => attempt.Reservation.MessageId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var row in rows)
            row.SetReplay(attempts.GetValueOrDefault(row.MessageId), preferences);
    }
}
