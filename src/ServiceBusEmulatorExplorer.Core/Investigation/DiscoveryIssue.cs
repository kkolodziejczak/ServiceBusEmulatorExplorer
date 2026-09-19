using Azure;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Core.Investigation;

internal static class DiscoveryIssue
{
    public static string ForNamespace(string operation, Exception exception) =>
        $"Namespace {operation} failed ({SafeTypeName(exception)}).";

    public static string ForCollection(EntityKind kind, Exception exception, string? topicName = null)
    {
        string scope = kind == EntityKind.Subscription && !string.IsNullOrWhiteSpace(topicName)
            ? $"subscriptions for topic '{topicName}'"
            : $"{kind.ToString().ToLowerInvariant()} collection";
        return $"Discovery of {scope} failed ({SafeTypeName(exception)}).";
    }

    public static string ForEntity(
        EntityKind kind,
        string entityName,
        string operation,
        Exception exception,
        string? topicName = null)
    {
        string address = kind == EntityKind.Subscription && !string.IsNullOrWhiteSpace(topicName)
            ? $"'{topicName}/subscriptions/{entityName}'"
            : $"'{entityName}'";
        return $"Discovery of {kind.ToString().ToLowerInvariant()} {address} {operation} failed ({SafeTypeName(exception)}).";
    }

    private static string SafeTypeName(Exception exception) => exception switch
    {
        RequestFailedException { Status: 401 } => "AuthenticationFailed (HTTP 401)",
        RequestFailedException { Status: 403 } => "AuthorizationFailed (HTTP 403)",
        RequestFailedException request when request.Status is >= 400 and <= 599
            => $"RequestFailed (HTTP {request.Status})",
        _ => exception.GetType().Name
    };
}
