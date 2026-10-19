using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

public sealed partial class EntityNode : ObservableObject
{
    private DateDisplayFormat dateFormat;
    public void SetDateFormat(DateDisplayFormat format) { dateFormat = format; NotifyCounts(); }
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

    // Removes, inserts and moves only what changed, so bound tree items and their selection survive.
    public static void ReconcileChildren(ObservableCollection<EntityNode> target, IReadOnlyList<EntityNode> desired)
    {
        for (int index = target.Count - 1; index >= 0; index--)
            if (!desired.Contains(target[index])) target.RemoveAt(index);
        for (int index = 0; index < desired.Count; index++)
        {
            int currentIndex = target.IndexOf(desired[index]);
            if (currentIndex < 0) target.Insert(index, desired[index]);
            else if (currentIndex != index) target.Move(currentIndex, index);
        }
    }
    public bool IsExpanded { get => expanded; set => SetProperty(ref expanded, value); }
    public bool IsVisible { get => visible; set => SetProperty(ref visible, value); }
    public string DisplayMessageCount => observedMain?.Display ?? Format(Observation?.Counts.Active);
    public string DisplayDlqCount => observedDlq?.Display ?? Format(Observation?.Counts.DeadLetter);
    public string DisplayScheduledCount => Format(Observation?.Counts.Scheduled);
    public string ActiveCountDetail => CountDetail("Messages", observedMain?.Exact ?? DisplayMessageCount,
        observedMain?.Detail(MessageBucket.Active, dateFormat) ?? Detail(Observation?.Counts.Active,
            "Active messages reported by the broker.\nEmulator counts may be inaccurate.")) + WatchDetail(IsActiveWatched, "Active");
    public string ScheduledCountDetail => CountDetail("Scheduled", DisplayScheduledCount,
        Detail(Observation?.Counts.Scheduled, "Scheduled messages reported by the broker."));
    public string DlqCountDetail => CountDetail("DLQ", observedDlq?.Exact ?? DisplayDlqCount,
        observedDlq?.Detail(MessageBucket.DeadLetter, dateFormat) ?? Detail(Observation?.Counts.DeadLetter,
            "Dead-letter messages reported by the broker.\nEmulator counts may be inaccurate.")) + WatchDetail(IsDlqWatched, "Dead letter");
    private string CountDetail(string label, string value, string detail)
    {
        if (IsGroup) return string.Empty;
        string entity = Kind == nameof(EntityKind.Subscription)
            ? $"Subscription: {Name}\nTopic: {Observation!.Entity.TopicName}" : $"{Kind}: {Name}";
        return $"{label}: {value}\n{entity}\n\n{detail}\n\n* Counted by peeking every message\n\u2014 Count unavailable";
    }
    public ObservedDeliveryCount? GetObservedCount(MessageBucket bucket) => bucket == MessageBucket.Active ? observedMain : observedDlq;
    public void SetObservedCount(MessageBucket bucket, ObservedDeliveryCount? count)
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
