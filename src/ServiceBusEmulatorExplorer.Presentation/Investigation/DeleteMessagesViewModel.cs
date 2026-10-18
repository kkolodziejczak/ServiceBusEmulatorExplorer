using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// Confirms deleting selected dead-letter deliveries. Only the exact word <see cref="ConfirmationWord"/> allows it,
/// and the targets are fixed when the confirmation opens, whatever the caller does with its own list afterwards.
/// </summary>
public sealed class DeleteMessagesViewModel : ObservableObject
{
    public const string ConfirmationWord = "DELETE";

    private string confirmationText = string.Empty;

    public DeleteMessagesViewModel(IReadOnlyList<MessageDelivery> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || targets.Any(target => target.Identity.Bucket != MessageBucket.DeadLetter)
            || targets.Select(target => target.Identity).Distinct().Count() != targets.Count)
            throw new ArgumentException("Select distinct dead-letter deliveries before confirming deletion.", nameof(targets));
        Targets = Array.AsReadOnly(targets.ToArray());

        string countLabel = $"{Targets.Count} message{(Targets.Count == 1 ? "" : "s")}";
        Heading = $"Delete {countLabel}?";
        Summary = $"Selected: {Targets.Count} dead-letter. Only these selected deliveries will be removed, including any unsaved edits to them.";
        ConfirmLabel = $"Delete {countLabel}";
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(() => Completed?.Invoke(false));
    }

    /// <summary>Raised with <c>true</c> when the user confirms and <c>false</c> when they cancel.</summary>
    public event Action<bool>? Completed;

    public IReadOnlyList<MessageDelivery> Targets { get; }

    public string Heading { get; }

    public string Summary { get; }

    public string ConfirmLabel { get; }

    public string ConfirmationText
    {
        get => confirmationText;
        set
        {
            if (SetProperty(ref confirmationText, value ?? string.Empty)) ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }

    private void Confirm()
    {
        if (CanConfirm()) Completed?.Invoke(true);
    }

    private bool CanConfirm() => ConfirmationText == ConfirmationWord;
}
