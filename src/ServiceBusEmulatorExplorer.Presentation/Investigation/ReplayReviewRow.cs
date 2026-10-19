using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>One copy in <see cref="ReplayReviewViewModel"/>: where it goes and the new message ID it will carry.</summary>
public sealed class ReplayReviewRow : ObservableObject
{
    private readonly Action idChanged;
    private string messageId;

    internal ReplayReviewRow(PreparedReplay prepared, Action idChanged, Action<ReplayReviewRow> editId)
    {
        Prepared = prepared;
        this.idChanged = idChanged;
        messageId = prepared.Reservation.MessageId;
        ResetCommand = new RelayCommand(() => MessageId = Prepared.Reservation.MessageId);
        EditIdCommand = new RelayCommand(() => editId(this));
    }

    public PreparedReplay Prepared { get; }

    public string MessageId
    {
        get => messageId;
        set
        {
            if (!SetProperty(ref messageId, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(HasProblem));
            idChanged();
        }
    }

    public string? Problem => ReplayAttempt.MessageIdProblem(MessageId);

    public bool HasProblem => Problem is not null;

    public string Source => Path(Prepared.Delivery.Identity.Source) + " · Dead letter";

    public string Destination => Path(ReplayLineage.Destination(Prepared.Delivery.Identity.Source))
        + (Prepared.Delivery.Identity.Source.Kind == EntityKind.Subscription ? " (Topic)" : " (Queue)");

    public string OriginalId => Prepared.Delivery.Message.MessageId;

    public string Notice => Prepared.Delivery.Identity.Source.Kind == EntityKind.Subscription
        ? "Other matching subscriptions may receive this copy. The original DLQ message stays."
        : "A new active copy will be sent. The original DLQ message stays.";

    public string BodySummary => Prepared.EditedBody is null ? "Original body" : "Modified JSON";

    /// <summary>Puts back the message ID generated when the replay was prepared.</summary>
    public RelayCommand ResetCommand { get; }

    public RelayCommand EditIdCommand { get; }

    private static string Path(EntityAddress source) => source.TopicName is null ? source.Name : $"{source.TopicName} / {source.Name}";
}
