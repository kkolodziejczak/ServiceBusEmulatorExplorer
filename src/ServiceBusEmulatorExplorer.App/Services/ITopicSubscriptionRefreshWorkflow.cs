using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.App.Services;

public interface ITopicSubscriptionRefreshWorkflow
{
    Task<TopicSubscriptionRefreshResult> RefreshAsync(
        string topicName,
        CancellationToken cancellationToken);
}

public sealed record TopicSubscriptionRefreshResult(
    IReadOnlyList<ServiceBusEntityNode> Entities,
    IReadOnlyList<RefreshedSubscriptionMessages> RefreshedSubscriptions,
    IReadOnlyList<TopicSubscriptionRefreshFailure> Failures);

public sealed record RefreshedSubscriptionMessages(
    ServiceBusEntityNode Subscription,
    IReadOnlyList<ExplorerMessage> ActiveMessages,
    IReadOnlyList<ExplorerMessage> DeadLetterMessages);

public sealed record TopicSubscriptionRefreshFailure(
    ServiceBusEntityNode Subscription,
    string ErrorMessage);
