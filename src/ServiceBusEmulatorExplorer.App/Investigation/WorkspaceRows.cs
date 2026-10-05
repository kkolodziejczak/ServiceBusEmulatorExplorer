using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed partial class EntityNode : ObservableObject
{
    private DateDisplayFormat dateFormat;
    internal void SetDateFormat(DateDisplayFormat format) { dateFormat = format; NotifyCounts(); }
    private bool expanded = true;
    private bool visible = true;
    private ObservedDeliveryCount? observedMain;
    private ObservedDeliveryCount? observedDlq;
    public EntityNode(string name, string kind, EntityObservation? observation = null)
    {
        Name = name;
        Kind = kind;
        Observation = observation;
    }
    public string Name { get; }
    public string Kind { get; }
    public EntityObservation? Observation { get; private set; }
    public bool IsGroup => Observation is null;
    public string Path => Observation?.Entity.Kind == EntityKind.Subscription
        ? $"{Observation.Entity.TopicName}/{Name}" : Name;
    public EntityAddress? Address => Observation is null ? null :
        new(Observation.Entity.Kind, Observation.Entity.Name, Observation.Entity.TopicName);
    public ObservableCollection<EntityNode> Children { get; } = [];
    public bool IsExpanded { get => expanded; set => SetProperty(ref expanded, value); }
    public bool IsVisible { get => visible; set => SetProperty(ref visible, value); }
    public string DisplayMessageCount => observedMain?.Display ?? Format(Observation?.Counts.Active);
    public string DisplayDlqCount => observedDlq?.Display ?? Format(Observation?.Counts.DeadLetter);
    public string DisplayScheduledCount => Format(Observation?.Counts.Scheduled);
    public string ActiveCountDetail => CountDetail("Messages", DisplayMessageCount,
        observedMain?.Detail(MessageBucket.Active, dateFormat) ?? Detail(Observation?.Counts.Active,
            "Active messages reported by the broker.\nEmulator counts may be inaccurate.")) + WatchDetail(IsActiveWatched, "Active");
    public string ScheduledCountDetail => CountDetail("Scheduled", DisplayScheduledCount,
        Detail(Observation?.Counts.Scheduled, "Scheduled messages reported by the broker."));
    public string DlqCountDetail => CountDetail("DLQ", DisplayDlqCount,
        observedDlq?.Detail(MessageBucket.DeadLetter, dateFormat) ?? Detail(Observation?.Counts.DeadLetter,
            "Dead-letter messages reported by the broker.\nEmulator counts may be inaccurate.")) + WatchDetail(IsDlqWatched, "Dead letter");
    private string CountDetail(string label, string value, string detail)
    {
        if (IsGroup) return string.Empty;
        string entity = Kind == nameof(EntityKind.Subscription)
            ? $"Subscription: {Name}\nTopic: {Observation!.Entity.TopicName}" : $"{Kind}: {Name}";
        return $"{label}: {value}\n{entity}\n\n{detail}\n\n* Observed while browsing\n\u2014 Count unavailable";
    }
    internal ObservedDeliveryCount? GetObservedCount(MessageBucket bucket) => bucket == MessageBucket.Active ? observedMain : observedDlq;
    internal void SetObservedCount(MessageBucket bucket, ObservedDeliveryCount? count)
    {
        if (bucket == MessageBucket.Active) observedMain = count;
        else observedDlq = count;
        NotifyCounts();
    }
    public void UpdateObservation(EntityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (Observation is not null && Address != new EntityAddress(
                observation.Entity.Kind, observation.Entity.Name, observation.Entity.TopicName))
            throw new ArgumentException("The observation address must match the existing entity.", nameof(observation));

        Observation = observation;
        if (observation.Counts.Active.Availability == CountAvailability.Stale) observedMain = null;
        if (observation.Counts.DeadLetter.Availability == CountAvailability.Stale) observedDlq = null;
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Address));
        NotifyCounts();
    }
    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(DisplayMessageCount));
        OnPropertyChanged(nameof(DisplayDlqCount));
        OnPropertyChanged(nameof(DisplayScheduledCount));
        OnPropertyChanged(nameof(ActiveCountDetail));
        OnPropertyChanged(nameof(ScheduledCountDetail));
        OnPropertyChanged(nameof(DlqCountDetail));
    }
    private static string Format(CountObservation? count) => count is null ? "" :
        count.Availability == CountAvailability.Known && count.Value is { } value ? value.ToString("N0") : "—";
    private static string Detail(CountObservation? count, string fallback) => count?.Detail ?? fallback;
}

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
