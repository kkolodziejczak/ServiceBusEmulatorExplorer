using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Investigation;

public sealed class EntityNode : ObservableObject
{
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
    public string ActiveCountDetail => observedMain?.Detail(MessageBucket.Active) ?? Detail(Observation?.Counts.Active, "Active messages reported by the broker. Emulator counts may be inaccurate.");
    public string ScheduledCountDetail => Detail(Observation?.Counts.Scheduled, "Scheduled messages reported by the broker.");
    public string DlqCountDetail => observedDlq?.Detail(MessageBucket.DeadLetter) ?? Detail(Observation?.Counts.DeadLetter, "Dead-letter messages reported by the broker. Emulator counts may be inaccurate.");
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
    private string observationDetail = string.Empty;
    public MessageRow(MessageDelivery delivery, TimestampDisplay display)
    {
        Delivery = delivery;
        timeDisplay = display;
    }
    public MessageDelivery Delivery { get; private set; }
    public DeliveryIdentity Key => Delivery.Identity;
    public string MessageId => Delivery.Message.MessageId;
    public string CorrelationId => Delivery.Message.CorrelationId ?? "";
    public string EventName => string.IsNullOrWhiteSpace(Delivery.Message.Subject) ? "Message" : Delivery.Message.Subject;
    public string Source => Delivery.Identity.Source.Kind == EntityKind.Subscription
        ? $"{Delivery.Identity.Source.TopicName}/{Delivery.Identity.Source.Name}" : Delivery.Identity.Source.Name;
    public bool IsDeadLetter => Delivery.Identity.Bucket == MessageBucket.DeadLetter;
    public string StateLabel => IsDeadLetter ? "DLQ" : "Active";
    public string ObservationDetail
    {
        get => observationDetail;
        private set => SetProperty(ref observationDetail, value);
    }
    public string DeadLetterReason => Delivery.Message.SystemProperties.TryGetValue("DeadLetterReason", out var reason) ? reason?.ToString() ?? "" : "";
    public DateTimeOffset? Enqueued => timeDisplay == TimestampDisplay.Local
        ? Delivery.Message.EnqueuedTime?.ToLocalTime() : Delivery.Message.EnqueuedTime?.ToUniversalTime();
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
    }

    public void MarkRetainedAsUnobserved()
    {
        ObservationDetail = "Retained from the previous refresh; this delivery was not returned by the broker.";
    }
    public void SetTimeDisplay(TimestampDisplay display)
    {
        timeDisplay = display;
        OnPropertyChanged(nameof(Enqueued));
    }
}

public sealed record ActivityEntry(DateTimeOffset TimestampUtc, string Message, bool Warning, bool Watch = false);
