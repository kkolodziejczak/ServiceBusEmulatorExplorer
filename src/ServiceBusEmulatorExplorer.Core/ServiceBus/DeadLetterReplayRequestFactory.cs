using ServiceBusEmulatorExplorer.Core.Messaging;

namespace ServiceBusEmulatorExplorer.Core.ServiceBus;

public static class DeadLetterReplayRequestFactory
{
    public static ReplayRequest CreateCopyRequest(ServiceBusEntityNode entity, ExplorerMessage message)
    {
        return CreateRequest(entity, message, edits: null);
    }

    public static ReplayRequest CreateEditedRequest(
        ServiceBusEntityNode entity,
        ExplorerMessage message,
        ReplayMessageEdits edits)
    {
        return CreateRequest(entity, message, edits);
    }

    private static ReplayRequest CreateRequest(
        ServiceBusEntityNode entity,
        ExplorerMessage message,
        ReplayMessageEdits? edits)
    {
        return new ReplayRequest(
            CreateSourceAddress(entity),
            CreateDestinationAddress(entity),
            message.SequenceNumber,
            edits?.EditedBody,
            edits?.ContentType,
            edits?.CorrelationId,
            edits?.SessionId,
            edits?.Subject,
            edits?.ApplicationProperties,
            ReplayIdPolicy.NewGuid,
            DeleteOriginal: false);
    }

    private static EntityAddress CreateSourceAddress(ServiceBusEntityNode entity)
    {
        return entity.Kind switch
        {
            EntityKind.Queue => new EntityAddress(EntityKind.Queue, entity.Name),
            EntityKind.Subscription => new EntityAddress(EntityKind.Subscription, entity.Name, entity.TopicName),
            _ => throw new ArgumentException("DLQ messages can be replayed from queues and subscriptions, not topics.", nameof(entity))
        };
    }

    private static EntityAddress CreateDestinationAddress(ServiceBusEntityNode entity)
    {
        return entity.Kind switch
        {
            EntityKind.Queue => new EntityAddress(EntityKind.Queue, entity.Name),
            EntityKind.Subscription => new EntityAddress(EntityKind.Topic, entity.TopicName!),
            _ => throw new ArgumentException("DLQ messages can be replayed to queues or topics, not subscriptions.", nameof(entity))
        };
    }
}
