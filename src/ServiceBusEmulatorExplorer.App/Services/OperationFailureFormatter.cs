using Azure;
using Azure.Identity;
using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.App.Services;

public sealed record OperationFailure(string UserMessage, string Detail);

public static class OperationFailureFormatter
{
    public static OperationFailure Format(Exception exception, ConnectionAuthenticationMode authenticationMode)
    {
        bool isAzureCli = authenticationMode == ConnectionAuthenticationMode.AzureCli;

        return exception switch
        {
            CredentialUnavailableException => new(
                "Azure CLI authentication is unavailable. Run az login, then reconnect.",
                "Credential details are omitted from the operation log."),
            AuthenticationFailedException => new(
                "Azure CLI authentication failed. Verify the active tenant and account with Azure CLI, then reconnect.",
                "Credential details are omitted from the operation log."),
            RequestFailedException { Status: 401 } requestFailedException => CreateAuthenticationFailure(requestFailedException, isAzureCli),
            RequestFailedException { Status: 403 } requestFailedException => CreateAuthorizationFailure(requestFailedException, isAzureCli),
            UnauthorizedAccessException => CreateAuthorizationFailure(isAzureCli),
            _ => new("The operation failed.", $"{exception.GetType().Name}; raw exception details are omitted.")
        };
    }

    private static OperationFailure CreateAuthenticationFailure(RequestFailedException exception, bool isAzureCli)
    {
        return new OperationFailure(
            isAzureCli
                ? "Authentication failed. Verify the active tenant and account with Azure CLI, then reconnect."
                : "Connection-string authentication failed. Verify the connection string and its SAS permissions, then reconnect.",
            CreateRequestDetail(exception));
    }

    private static OperationFailure CreateAuthorizationFailure(RequestFailedException exception, bool isAzureCli)
    {
        return CreateAuthorizationFailure(isAzureCli, CreateRequestDetail(exception));
    }

    private static OperationFailure CreateAuthorizationFailure(bool isAzureCli)
    {
        return CreateAuthorizationFailure(isAzureCli, "UnauthorizedAccessException; raw exception details are omitted.");
    }

    private static OperationFailure CreateAuthorizationFailure(bool isAzureCli, string detail)
    {
        return new OperationFailure(
            isAzureCli
                ? "The current account does not have permission for this operation. Request Azure Service Bus Data Owner at namespace scope, then retry."
                : "Connection-string authorization failed. Verify the connection string and its SAS permissions, then retry.",
            detail);
    }

    private static string CreateRequestDetail(RequestFailedException exception)
    {
        return string.IsNullOrWhiteSpace(exception.ErrorCode)
            ? $"HTTP {exception.Status}."
            : $"HTTP {exception.Status} ({exception.ErrorCode}).";
    }

}
