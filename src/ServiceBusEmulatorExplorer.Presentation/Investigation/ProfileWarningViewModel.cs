using CommunityToolkit.Mvvm.Input;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// A profile-coloured question that names the connection profile it concerns: the connection warning, discarding
/// drafts, and removing a replay from history. Cancel is the safe answer.
/// </summary>
public sealed class ProfileWarningViewModel
{
    public ProfileWarningViewModel(string profileName, string message, string heading = "Connection warning",
        string continueLabel = "Continue")
    {
        ArgumentNullException.ThrowIfNull(profileName);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(continueLabel);
        ProfileName = profileName;
        Message = message;
        Heading = heading;
        ContinueLabel = continueLabel;
        ContinueCommand = new RelayCommand(() => Completed?.Invoke(true));
        CancelCommand = new RelayCommand(() => Completed?.Invoke(false));
    }

    /// <summary>Raised with <c>true</c> when the user continues and <c>false</c> when they cancel.</summary>
    public event Action<bool>? Completed;

    public string ProfileName { get; }

    public string Message { get; }

    /// <summary>The heading, which is also the window title.</summary>
    public string Heading { get; }

    public string ContinueLabel { get; }

    public RelayCommand ContinueCommand { get; }

    public RelayCommand CancelCommand { get; }
}
