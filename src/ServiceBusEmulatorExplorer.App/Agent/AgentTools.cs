using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Agent;

/// <summary>
/// Read-only MCP tools over the live session (specs/agent-access.md). Every tool reads permission
/// together with state; a blocked profile yields no profile, entity or message data (DEC-025).
/// </summary>
public static class AgentTools
{
    public const int MaxPeek = 50;
    public const int FocusedBodyLimit = 256 * 1024;
    public const int ListedBodyLimit = 16 * 1024;
    private const int VisibleLimit = 100;

    public const string Instructions =
        "Service Bus Emulator Explorer is a desktop app for Azure Service Bus and its local emulator. " +
        "These tools read what the app currently sees for the active connection profile. They never change messages or entities. " +
        "Start with get_app_state. To diagnose a dead-lettered message, read get_focused_message (DeadLetterReason, " +
        "DeadLetterErrorDescription, delivery count) and compare it with the entity settings from list_entities, such as maxDeliveryCount and lockDuration. " +
        "Poll get_watch_arrivals with the returned cursor to see new messages in watched entities. " +
        "All times are UTC. Emulator message counts may be unavailable; never treat an unavailable count as zero.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static McpServerPrimitiveCollection<McpServerTool> Create(IAgentStateSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return
        [
            McpServerTool.Create(
                async (CancellationToken cancellationToken) => Serialize(AppState(await source.CaptureAsync(cancellationToken))),
                Options("get_app_state", "Current app state: connection profile name, connection health, selected entity and bucket, " +
                    "the focused message's identity, the visible message list (summaries), and Watch rules. Reports only 'blocked' when the " +
                    "active profile does not allow agent access.")),
            McpServerTool.Create(
                async (CancellationToken cancellationToken) =>
                {
                    var snapshot = Allowed(await source.CaptureAsync(cancellationToken));
                    return snapshot.Focused is null
                        ? Serialize(new { focused = (object?)null, hint = "No message is open in the inspector. Ask the user to select one." })
                        : Serialize(new { focused = Message(snapshot.Focused, FocusedBodyLimit), capturedAtUtc = snapshot.CapturedAtUtc });
                },
                Options("get_focused_message", "The message open in the app's inspector, with full body (capped), application and system " +
                    "properties, DeadLetterReason, DeadLetterErrorDescription, DeadLetterSource, delivery count and UTC times.")),
            McpServerTool.Create(
                async (CancellationToken cancellationToken) =>
                {
                    var snapshot = Allowed(await source.CaptureAsync(cancellationToken));
                    if (snapshot.Discovery is null) return Serialize(new { connected = snapshot.Connected, entities = Array.Empty<object>() });
                    return Serialize(new
                    {
                        observedAtUtc = snapshot.Discovery.ObservedAtUtc,
                        isComplete = snapshot.Discovery.IsComplete,
                        issues = snapshot.Discovery.Issues,
                        entities = snapshot.Discovery.Entities.Select(Entity)
                    });
                },
                Options("list_entities", "Queues, topics and subscriptions with their settings (maxDeliveryCount, lockDuration, " +
                    "defaultMessageTimeToLive, requiresSession, requiresDuplicateDetection) and message counts with availability.")),
            McpServerTool.Create(
                async (
                    [Description("Entity path from list_entities: a queue name, or 'topic/subscription'. Topics cannot be peeked.")] string entity,
                    [Description("'active' or 'deadLetter'.")] string bucket,
                    [Description("Number of messages, 1-50. Default 10.")] int? take = null,
                    [Description("Start at this sequence number. Omit to start at the oldest.")] long? fromSequenceNumber = null,
                    CancellationToken cancellationToken = default) =>
                {
                    var parsed = ParseBucket(bucket);
                    int count = Math.Clamp(take ?? 10, 1, MaxPeek);
                    var deliveries = await Guard(() => source.PeekAsync(entity, parsed, count, fromSequenceNumber, cancellationToken));
                    return Serialize(new
                    {
                        entity,
                        bucket = parsed,
                        messages = deliveries.Select(delivery => Message(delivery, ListedBodyLimit)),
                        nextFromSequenceNumber = deliveries.Count == count ? deliveries[^1].Message.SequenceNumber + 1 : (long?)null
                    });
                },
                Options("peek_messages", "Peeks (does not receive or remove) messages from a queue or subscription, Active or dead-letter. " +
                    "Bodies over 16 KB are truncated.")),
            McpServerTool.Create(
                async (
                    [Description("Cursor returned by the previous call. Omit on the first call.")] string? cursor = null,
                    [Description("Maximum arrivals, 1-100. Default 50.")] int? limit = null,
                    CancellationToken cancellationToken = default) =>
                {
                    var page = await Guard(() => source.ReadArrivalsAsync(cursor, limit ?? 50, cancellationToken));
                    return Serialize(new
                    {
                        cursor = page.NextCursor,
                        expired = page.Expired ? true : (bool?)null,
                        note = page.Expired ? "The previous cursor is no longer valid (reconnect, profile switch or evicted arrivals). Arrivals start again from the oldest retained one." : null,
                        hasMore = page.HasMore,
                        arrivals = page.Arrivals.Select(arrival => new
                        {
                            acceptedAtUtc = arrival.AcceptedAtUtc,
                            message = Message(arrival.Delivery, ListedBodyLimit)
                        })
                    });
                },
                Options("get_watch_arrivals", "New messages that the app's Watch observed in watched entities, oldest first. " +
                    "Pass the returned cursor next time to get only newer arrivals. Watch polls about every 15 seconds."))
        ];
    }

    private static McpServerToolCreateOptions Options(string name, string description) =>
        new() { Name = name, Description = description, ReadOnly = true, Destructive = false, OpenWorld = false };

    private static AgentSnapshot Allowed(AgentSnapshot snapshot) =>
        snapshot.Allowed ? snapshot : throw new McpException(AgentAccessBlockedException.UserMessage);

    private static async Task<T> Guard<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (AgentAccessBlockedException) { throw new McpException(AgentAccessBlockedException.UserMessage); }
        catch (InvalidOperationException exception) { throw new McpException(exception.Message); }
    }

    private static MessageBucket ParseBucket(string bucket) => bucket?.Trim().ToLowerInvariant() switch
    {
        "active" => MessageBucket.Active,
        "deadletter" or "dead-letter" or "dlq" => MessageBucket.DeadLetter,
        _ => throw new McpException("bucket must be 'active' or 'deadLetter'.")
    };

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    public static object AppState(AgentSnapshot snapshot)
    {
        if (!snapshot.Allowed) return new { agentAccess = "blocked", reason = AgentAccessBlockedException.UserMessage };
        var visible = snapshot.Visible ?? [];
        return new
        {
            agentAccess = "allowed",
            capturedAtUtc = snapshot.CapturedAtUtc,
            profile = new { name = snapshot.ProfileName, isEmulator = snapshot.IsEmulator },
            connection = new { connected = snapshot.Connected, health = snapshot.Health, detail = snapshot.HealthDetail },
            view = new
            {
                mode = snapshot.SearchActive ? "search" : "browse",
                entity = snapshot.SelectedEntityPath,
                bucket = snapshot.SelectedBucket,
                focusedMessage = snapshot.Focused is null ? null : Summary(snapshot.Focused),
                visibleMessageCount = visible.Count,
                visibleMessages = visible.Take(VisibleLimit).Select(Summary),
                visibleTruncated = visible.Count > VisibleLimit ? true : (bool?)null
            },
            watch = new
            {
                rules = (snapshot.WatchRules ?? []).Select(rule => new { scope = rule.ScopeKey, active = rule.Active, deadLetter = rule.DeadLetter, included = rule.Included }),
                pendingArrivalsInApp = snapshot.PendingWatchArrivals
            }
        };
    }

    private static object Summary(MessageDelivery delivery) => new
    {
        entity = Path(delivery.Identity.Source),
        bucket = delivery.Identity.Bucket,
        sequenceNumber = delivery.Message.SequenceNumber,
        messageId = delivery.Message.MessageId,
        correlationId = delivery.Message.CorrelationId,
        subject = delivery.Message.Subject,
        enqueuedTimeUtc = delivery.Message.EnqueuedTime?.ToUniversalTime(),
        deliveryCount = delivery.Message.DeliveryCount,
        deadLetterReason = Text(delivery.Message.SystemProperties, "DeadLetterReason")
    };

    public static object Message(MessageDelivery delivery, int bodyLimit)
    {
        var message = delivery.Message;
        string body = message.Body ?? string.Empty;
        bool truncated = body.Length > bodyLimit;
        return new
        {
            entity = Path(delivery.Identity.Source),
            bucket = delivery.Identity.Bucket,
            sequenceNumber = message.SequenceNumber,
            messageId = message.MessageId,
            correlationId = message.CorrelationId,
            sessionId = message.SessionId,
            subject = message.Subject,
            contentType = message.ContentType,
            enqueuedTimeUtc = message.EnqueuedTime?.ToUniversalTime(),
            expiresAtUtc = message.ExpiresAt?.ToUniversalTime(),
            deliveryCount = message.DeliveryCount,
            deadLetterReason = Text(message.SystemProperties, "DeadLetterReason"),
            deadLetterErrorDescription = Text(message.SystemProperties, "DeadLetterErrorDescription"),
            deadLetterSource = Text(message.SystemProperties, "DeadLetterSource"),
            bodySizeBytes = message.BodySizeBytes,
            body = truncated ? body[..bodyLimit] : body,
            bodyTruncated = truncated ? true : (bool?)null,
            applicationProperties = Values(message.ApplicationProperties),
            systemProperties = Values(message.SystemProperties)
        };
    }

    private static object Entity(EntityObservation observation)
    {
        var entity = observation.Entity;
        var metadata = entity.Metadata;
        return new
        {
            path = entity.Kind == EntityKind.Subscription ? $"{entity.TopicName}/{entity.Name}" : entity.Name,
            kind = entity.Kind,
            status = metadata.Status,
            maxDeliveryCount = metadata.MaxDeliveryCount,
            lockDuration = Duration(metadata.LockDuration),
            defaultMessageTimeToLive = Duration(metadata.DefaultMessageTimeToLive),
            requiresSession = metadata.RequiresSession,
            requiresDuplicateDetection = metadata.RequiresDuplicateDetection,
            createdAtUtc = metadata.CreatedAtUtc?.ToUniversalTime(),
            updatedAtUtc = metadata.UpdatedAtUtc?.ToUniversalTime(),
            counts = new
            {
                active = Count(observation.Counts.Active),
                deadLetter = Count(observation.Counts.DeadLetter),
                scheduled = Count(observation.Counts.Scheduled)
            }
        };
    }

    private static object Count(CountObservation count) =>
        new { value = count.Value, availability = count.Availability, detail = count.Detail };

    private static string Path(EntityAddress address) =>
        address.Kind == EntityKind.Subscription ? $"{address.TopicName}/{address.Name}" : address.Name;

    // ISO 8601 durations; TimeSpan.MaxValue means "never expires" in Service Bus.
    private static string? Duration(TimeSpan? value) =>
        value is null ? null : value.Value >= TimeSpan.FromDays(10_000) ? "unlimited" : XmlConvert.ToString(value.Value);

    private static string? Text(IReadOnlyDictionary<string, object?> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null && value.ToString() is { Length: > 0 } text ? text : null;

    private static Dictionary<string, object?> Values(IReadOnlyDictionary<string, object?> properties) =>
        properties.ToDictionary(pair => pair.Key, pair => pair.Value switch
        {
            null => null,
            string or bool or int or long or double or float or decimal or short or byte => pair.Value,
            DateTimeOffset time => time.ToUniversalTime(),
            DateTime time => new DateTimeOffset(time.ToUniversalTime(), TimeSpan.Zero),
            TimeSpan span => XmlConvert.ToString(span),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => pair.Value.ToString()
        });
}
