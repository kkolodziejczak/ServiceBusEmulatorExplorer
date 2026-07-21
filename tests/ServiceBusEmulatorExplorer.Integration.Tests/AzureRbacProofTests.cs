using ServiceBusEmulatorExplorer.Core.Connection;
using ServiceBusEmulatorExplorer.Core.ServiceBus;
using ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

namespace ServiceBusEmulatorExplorer.Integration.Tests;

public sealed class AzureRbacProofTests
{
    [AzureRbacProofFact]
    [Trait("TestCategory", "AzureRbac")]
    public async Task Preprovisioned_topic_and_subscription_support_browse_peek_and_send()
    {
        string fullyQualifiedNamespace = RequiredEnvironmentVariable("SBE_AZURE_NAMESPACE");
        string topicName = RequiredEnvironmentVariable("SBE_AZURE_TOPIC");
        string subscriptionName = RequiredEnvironmentVariable("SBE_AZURE_SUBSCRIPTION");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var clientFactory = new DirectServiceBusClientFactory();
        await clientFactory.ConnectAsync(
            new ConnectionProfile("Azure RBAC proof", "", "", ConnectionAuthenticationMode.AzureCli, fullyQualifiedNamespace),
            timeout.Token);
        var administrationService = new ServiceBusAdministrationService(clientFactory);
        var messageService = new ServiceBusMessageService(clientFactory);

        IReadOnlyList<ServiceBusEntityNode> entities = await administrationService.GetEntityTreeAsync(timeout.Token);
        ServiceBusEntityNode topic = Assert.Single(entities, entity =>
            entity.Kind == EntityKind.Topic && string.Equals(entity.Name, topicName, StringComparison.OrdinalIgnoreCase));
        ServiceBusEntityNode subscription = Assert.Single(entities, entity =>
            entity.Kind == EntityKind.Subscription
            && string.Equals(entity.TopicName, topic.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entity.Name, subscriptionName, StringComparison.OrdinalIgnoreCase));

        await messageService.PeekMessagesAsync(
            new EntityAddress(EntityKind.Subscription, subscription.Name, topic.Name),
            MessageBucket.Active,
            take: 1,
            fromSequenceNumber: null,
            timeout.Token);

        string proofId = Guid.NewGuid().ToString("N");
        await messageService.SendMessageAsync(
            new SendMessageCommand(
                new EntityAddress(EntityKind.Topic, topic.Name),
                $"Service Bus Explorer Azure RBAC proof {proofId}",
                ContentType: "text/plain",
                ApplicationProperties: new Dictionary<string, object?> { ["sbe-proof-id"] = proofId }),
            timeout.Token);
    }

    private static string RequiredEnvironmentVariable(string variable)
    {
        return Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"{variable} must be set by AzureRbacProofFactAttribute.");
    }
}
