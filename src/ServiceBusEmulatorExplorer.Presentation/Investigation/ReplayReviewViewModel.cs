using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// Reviews prepared dead-letter replays before anything is sent. Each copy's new message ID can be edited, and Replay
/// is available only while every ID is valid. The caller sends each candidate with the ID at the same index of
/// <see cref="ReviewedIds"/>.
/// </summary>
public sealed class ReplayReviewViewModel
{
    public ReplayReviewViewModel(string profileName, IReadOnlyList<PreparedReplay> candidates)
    {
        ArgumentNullException.ThrowIfNull(profileName);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 || candidates.Any(candidate => candidate is null))
            throw new ArgumentException("Select messages to replay.", nameof(candidates));
        ProfileName = profileName;
        Heading = candidates.Count == 1 ? "Replay 1 message" : $"Replay {candidates.Count} messages";
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(() => Completed?.Invoke(false));
        Rows = Array.AsReadOnly(candidates.Select(candidate => new ReplayReviewRow(candidate, IdChanged, EditId)).ToArray());
    }

    /// <summary>Raised with <c>true</c> when the user confirms and <c>false</c> when they cancel.</summary>
    public event Action<bool>? Completed;

    /// <summary>Raised when the user asks to edit a row's message ID; the view moves focus to that row's ID field.</summary>
    public event Action<ReplayReviewRow>? IdFocusRequested;

    /// <summary>The dialog heading, which is also the confirm button's label.</summary>
    public string Heading { get; }

    public string ProfileName { get; }

    public IReadOnlyList<ReplayReviewRow> Rows { get; }

    public IReadOnlyList<string> ReviewedIds => Rows.Select(row => row.MessageId).ToArray();

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }

    private void IdChanged() => ConfirmCommand.NotifyCanExecuteChanged();

    private void EditId(ReplayReviewRow row) => IdFocusRequested?.Invoke(row);

    private void Confirm()
    {
        if (CanConfirm()) Completed?.Invoke(true);
    }

    private bool CanConfirm() => Rows.All(row => row.Problem is null);
}
