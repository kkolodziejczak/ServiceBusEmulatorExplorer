using Azure;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

/// <summary>
/// Reads namespace entities and their runtime properties while retaining the
/// metadata that was successfully discovered when an individual request fails.
/// </summary>
public sealed class InvestigationEntityBrowser(IServiceBusClientFactory clientFactory) : IInvestigationEntityBrowser
{
    private const string EmulatorCountUnavailableDetail =
        "The local Service Bus emulator does not provide reliable runtime message counts; browse messages to inspect current contents.";

    private static readonly CountObservation UnsupportedScheduledCount =
        new(null, CountAvailability.NotSupported, "The Service Bus administration API does not expose scheduled counts for subscriptions.");

    public async Task<EntityDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);

        var observations = new List<EntityObservation>();
        var issues = new List<string>();
        bool isComplete = true;
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;

        ServiceBusAdministrationClient client;
        try
        {
            client = clientFactory.AdministrationClient;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            issues.Add(DiscoveryIssue.ForNamespace("administration client", exception));
            return new EntityDiscoverySnapshot(observations, observedAtUtc, false, issues);
        }

        await DiscoverQueuesAsync(client, observations, issues, cancellationToken, clientFactory.SupportsRuntimeCounts);
        await DiscoverTopicsAsync(client, observations, issues, cancellationToken, clientFactory.SupportsRuntimeCounts);

        isComplete = issues.Count == 0;
        return new EntityDiscoverySnapshot(
            observations
                .OrderBy(observation => KindOrder(observation.Entity.Kind))
                .ThenBy(observation => observation.Entity.TopicName ?? observation.Entity.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    observation => observation.Entity.Kind == EntityKind.Subscription
                        ? observation.Entity.Name
                        : string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .ToList(),
            observedAtUtc,
            isComplete,
            issues);
    }

    private static int KindOrder(EntityKind kind) => kind switch
    {
        EntityKind.Queue => 0,
        EntityKind.Topic => 1,
        EntityKind.Subscription => 2,
        _ => 3
    };

    private static async Task DiscoverQueuesAsync(
        ServiceBusAdministrationClient client,
        List<EntityObservation> observations,
        List<string> issues,
        CancellationToken cancellationToken,
        bool supportsRuntimeCounts)
    {
        try
        {
            await foreach (QueueProperties queue in client.GetQueuesAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                QueueRuntimeProperties? runtime = null;
                CountObservation active = Unavailable("Queue runtime properties are unavailable.");
                CountObservation deadLetter = Unavailable("Queue runtime properties are unavailable.");
                CountObservation scheduled = Unavailable("Queue runtime properties are unavailable.");

                try
                {
                    runtime = (await client.GetQueueRuntimePropertiesAsync(queue.Name, cancellationToken)).Value;
                    if (supportsRuntimeCounts)
                    {
                        active = Known(runtime.ActiveMessageCount);
                        deadLetter = Known(runtime.DeadLetterMessageCount);
                        scheduled = Known(runtime.ScheduledMessageCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    issues.Add(DiscoveryIssue.ForEntity(EntityKind.Queue, queue.Name, "runtime properties", exception));
                }
                if (!supportsRuntimeCounts)
                {
                    active = EmulatorUnavailable();
                    deadLetter = EmulatorUnavailable();
                    scheduled = EmulatorUnavailable();
                }

                observations.Add(new EntityObservation(
                    DiscoveryEntityMapper.Queue(queue, runtime),
                    new EntityCountObservation(active, deadLetter, scheduled)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            issues.Add(DiscoveryIssue.ForCollection(EntityKind.Queue, exception));
        }
    }

    private static async Task DiscoverTopicsAsync(
        ServiceBusAdministrationClient client,
        List<EntityObservation> observations,
        List<string> issues,
        CancellationToken cancellationToken,
        bool supportsRuntimeCounts)
    {
        try
        {
            await foreach (TopicProperties topic in client.GetTopicsAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TopicRuntimeProperties? runtime = null;
                CountObservation scheduled = Unavailable("Topic runtime properties are unavailable.");

                try
                {
                    runtime = (await client.GetTopicRuntimePropertiesAsync(topic.Name, cancellationToken)).Value;
                    if (supportsRuntimeCounts)
                    {
                        scheduled = Known(runtime.ScheduledMessageCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    issues.Add(DiscoveryIssue.ForEntity(EntityKind.Topic, topic.Name, "runtime properties", exception));
                }
                if (!supportsRuntimeCounts)
                {
                    scheduled = EmulatorUnavailable();
                }

                var subscriptions = new List<EntityObservation>();
                bool subscriptionsComplete = await DiscoverSubscriptionsAsync(
                    client,
                    topic,
                    subscriptions,
                    issues,
                    cancellationToken,
                    supportsRuntimeCounts);

                observations.Add(new EntityObservation(
                    DiscoveryEntityMapper.Topic(topic, runtime),
                    CreateTopicCounts(subscriptions, subscriptionsComplete, scheduled, supportsRuntimeCounts)));
                observations.AddRange(subscriptions);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            issues.Add(DiscoveryIssue.ForCollection(EntityKind.Topic, exception));
        }
    }

    private static async Task<bool> DiscoverSubscriptionsAsync(
        ServiceBusAdministrationClient client,
        TopicProperties topic,
        List<EntityObservation> observations,
        List<string> issues,
        CancellationToken cancellationToken,
        bool supportsRuntimeCounts)
    {
        bool isComplete = true;
        try
        {
            await foreach (SubscriptionProperties subscription in client
                .GetSubscriptionsAsync(topic.Name, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SubscriptionRuntimeProperties? runtime = null;
                CountObservation active = Unavailable("Subscription runtime properties are unavailable.");
                CountObservation deadLetter = Unavailable("Subscription runtime properties are unavailable.");

                try
                {
                    runtime = (await client.GetSubscriptionRuntimePropertiesAsync(
                        topic.Name,
                        subscription.SubscriptionName,
                        cancellationToken)).Value;
                    if (supportsRuntimeCounts)
                    {
                        active = Known(runtime.ActiveMessageCount);
                        deadLetter = Known(runtime.DeadLetterMessageCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    isComplete = false;
                    issues.Add(DiscoveryIssue.ForEntity(
                        EntityKind.Subscription,
                        subscription.SubscriptionName,
                        "runtime properties",
                        exception,
                        topic.Name));
                }
                if (!supportsRuntimeCounts)
                {
                    active = EmulatorUnavailable();
                    deadLetter = EmulatorUnavailable();
                }

                observations.Add(new EntityObservation(
                    DiscoveryEntityMapper.Subscription(topic.Name, subscription, runtime),
                    new EntityCountObservation(active, deadLetter, UnsupportedScheduledCount)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            isComplete = false;
            issues.Add(DiscoveryIssue.ForCollection(EntityKind.Subscription, exception, topic.Name));
        }

        return isComplete;
    }

    private static EntityCountObservation CreateTopicCounts(
        IReadOnlyList<EntityObservation> subscriptions,
        bool subscriptionsComplete,
        CountObservation scheduled,
        bool supportsRuntimeCounts)
    {
        if (!supportsRuntimeCounts)
        {
            return new EntityCountObservation(
                EmulatorUnavailable(),
                EmulatorUnavailable(),
                scheduled);
        }

        CountObservation active = AggregateSubscriptionCount(subscriptions, subscriptionsComplete, subscription => subscription.Counts.Active, "active");
        CountObservation deadLetter = AggregateSubscriptionCount(subscriptions, subscriptionsComplete, subscription => subscription.Counts.DeadLetter, "dead-letter");
        return new EntityCountObservation(active, deadLetter, scheduled);
    }

    private static CountObservation AggregateSubscriptionCount(
        IReadOnlyList<EntityObservation> subscriptions,
        bool subscriptionsComplete,
        Func<EntityObservation, CountObservation> selector,
        string label)
    {
        if (!subscriptionsComplete)
        {
            return Unavailable($"Topic {label} count is unavailable because subscription discovery was incomplete.");
        }

        CountObservation[] values = subscriptions.Select(selector).ToArray();
        if (values.Any(value => value.Availability != CountAvailability.Known || value.Value is null))
        {
            return Unavailable($"Topic {label} count is unavailable because one or more subscription counts are unavailable.");
        }

        return Known(values.Sum(value => value.Value!.Value));
    }

    private static CountObservation Known(long value) => new(value, CountAvailability.Known);

    private static CountObservation Unavailable(string detail) => new(null, CountAvailability.Unavailable, detail);

    private static CountObservation EmulatorUnavailable() => Unavailable(EmulatorCountUnavailableDetail);
}
