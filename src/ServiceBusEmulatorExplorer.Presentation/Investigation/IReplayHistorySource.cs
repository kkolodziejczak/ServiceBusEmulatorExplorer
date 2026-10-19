using System.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// What Replay history reads from the investigation workspace and the one history change it makes. Changes to
/// <see cref="Preferences"/>, <see cref="IsConnected"/> and <see cref="SelectedProfile"/> are announced, possibly
/// from a background thread.
/// </summary>
public interface IReplayHistorySource : INotifyPropertyChanged
{
    WorkspacePreferences Preferences { get; }

    InvestigationProfile SelectedProfile { get; }

    bool IsConnected { get; }

    /// <summary>The selected profile's saved replay attempts.</summary>
    IReadOnlyList<ReplayAttempt> CurrentReplayHistory { get; }

    bool CanRemoveReplayHistory(ReplayAttempt requested);

    bool CanReviewReplayOriginalCleanup(ReplayAttempt requested);

    /// <summary>Removes an attempt from local history only; returns <c>false</c> when it was not removed.</summary>
    Task<bool> RemoveReplayHistoryAsync(ReplayAttempt requested, CancellationToken cancellationToken = default);

    void LogWarning(string message);
}
