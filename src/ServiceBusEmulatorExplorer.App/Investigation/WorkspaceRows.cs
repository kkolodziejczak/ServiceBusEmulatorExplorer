using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Presentation;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class MessageRow : ObservableObject
{
    private bool selected;
    private TimestampDisplay timeDisplay;
    private DateDisplayFormat dateFormat;
    private string observationDetail = string.Empty;
    private string replayDetail = string.Empty;
    public MessageRow(MessageDelivery delivery, TimestampDisplay display, DateDisplayFormat dateFormat = DateDisplayFormat.Windows)
    {
        Delivery = delivery;
        timeDisplay = display;
        this.dateFormat = dateFormat;
    }
    public MessageDelivery Delivery { get; private set; }
    public DeliveryIdentity Key => Delivery.Identity;
    public string MessageId => Delivery.Message.MessageId;
    public string CorrelationId => Delivery.Message.CorrelationId ?? "";
    public string EventName => string.IsNullOrWhiteSpace(Delivery.Message.Subject) ? "Message" : Delivery.Message.Subject;
    public string Source => Delivery.Identity.Source.Kind == EntityKind.Subscription
        ? $"{Delivery.Identity.Source.TopicName}/{Delivery.Identity.Source.Name}" : Delivery.Identity.Source.Name;
    public EntityKind SourceKind => Delivery.Identity.Source.Kind;
    public bool IsSubscription => SourceKind == EntityKind.Subscription;
    public string SourceTopic => Delivery.Identity.Source.TopicName ?? string.Empty;
    public string SourceName => Delivery.Identity.Source.Name;
    public string SourceDetail => IsSubscription
        ? $"Topic: {SourceTopic}\nSubscription: {SourceName}" : $"{SourceKind}: {SourceName}";
    public bool IsDeadLetter => Delivery.Identity.Bucket == MessageBucket.DeadLetter;
    public string StateLabel => IsDeadLetter ? "DLQ" : "Active";
    public bool IsReplay => replayDetail.Length > 0;
    public string ReplayDetail => replayDetail;
    internal void SetReplay(ReplayAttempt? attempt, WorkspacePreferences preferences)
    {
        string detail = string.Empty;
        if (attempt is not null)
        {
            var instant = attempt.SentAtUtc ?? attempt.RequestedAtUtc;
            if (preferences.TimestampDisplay == TimestampDisplay.Local) instant = instant.ToLocalTime();
            string zone = preferences.TimestampDisplay == TimestampDisplay.Local ? "Local" : "UTC";
            string outcome = attempt.SendStatus == ReplaySendStatus.Confirmed ? "Sent" : "Send uncertain; requested";
            detail = $"Replay\nOriginal ID: {attempt.OriginalMessageId}\n{outcome}: {DateDisplay.Timestamp(instant, preferences.DateFormat)} {zone}";
        }
        if (SetProperty(ref replayDetail, detail, nameof(ReplayDetail))) OnPropertyChanged(nameof(IsReplay));
    }
    public string ObservationDetail
    {
        get => observationDetail;
        private set => SetProperty(ref observationDetail, value);
    }
    public string DeadLetterReason => Delivery.Message.SystemProperties.TryGetValue("DeadLetterReason", out var reason) ? reason?.ToString() ?? "" : "";
    public DateTimeOffset? Enqueued => timeDisplay == TimestampDisplay.Local
        ? Delivery.Message.EnqueuedTime?.ToLocalTime() : Delivery.Message.EnqueuedTime?.ToUniversalTime();
    public string EnqueuedDisplay => Enqueued is { } instant ? DateDisplay.Timestamp(instant, dateFormat) : string.Empty;
    public bool IsSelected { get => selected; set => SetProperty(ref selected, value); }
    public void UpdateDelivery(MessageDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (Delivery.Identity != delivery.Identity)
            throw new ArgumentException("The delivery identity must match the existing row.", nameof(delivery));

        Delivery = delivery;
        ObservationDetail = string.Empty;
        OnPropertyChanged(nameof(Delivery));
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(MessageId));
        OnPropertyChanged(nameof(CorrelationId));
        OnPropertyChanged(nameof(EventName));
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(IsDeadLetter));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(DeadLetterReason));
        OnPropertyChanged(nameof(Enqueued));
        OnPropertyChanged(nameof(EnqueuedDisplay));
    }

    public void MarkRetainedAsUnobserved()
    {
        ObservationDetail = "Retained from the previous refresh; this delivery was not returned by the broker.";
    }
    public void SetTimeDisplay(TimestampDisplay display, DateDisplayFormat dateFormat = DateDisplayFormat.Windows)
    {
        timeDisplay = display;
        this.dateFormat = dateFormat;
        OnPropertyChanged(nameof(Enqueued));
        OnPropertyChanged(nameof(EnqueuedDisplay));
    }
}

public sealed record ActivityEntry(DateTimeOffset TimestampUtc, string Message, bool Warning, bool Watch = false, string? StatusSummary = null);
