using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.App.Services;

public sealed record OperationFailure(string UserMessage, string Detail);

public static class OperationFailureFormatter
{
    private static readonly Regex ConnectionStringPattern = new(
        @"(?ix)\bEndpoint\s*=\s*[^;\r\n]+(?:;[^;\r\n=]+\s*=\s*[^;\r\n]*)*;?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CredentialPattern = new(
        @"(?ix)(?:\b(?:[\w-]*(?:token|secret|password|passphrase|key|signature|sig)[\w-]*|connectionString)""?\s*[:=]\s*|\bBearer\s+)(?:""[^""]*""|'[^']*'|[^;\s,""']+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
            _ => new("The operation failed.", CreateGenericDetail(exception))
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

    private static string CreateGenericDetail(Exception exception)
    {
        var details = new List<string>();
        Exception? current = exception;
        int depth = 0;

        while (current is not null && depth++ < 5)
        {
            string typeName = current.GetType().Name;
            if (CanIncludeMessage(current))
            {
                string message = Sanitize(current.Message);
                details.Add(string.IsNullOrWhiteSpace(message)
                    ? typeName
                    : $"{typeName}: {message}");
            }
            else
            {
                details.Add($"{typeName}; raw exception details are omitted.");
            }

            current = current.InnerException;
        }

        return string.Join(" -> ", details);
    }

    private static bool CanIncludeMessage(Exception exception)
    {
        return exception is ServiceBusException
            or HttpRequestException
            or SocketException
            or TimeoutException
            or IOException;
    }

    private static string Sanitize(string message)
    {
        string sanitized = ConnectionStringPattern.Replace(message, "[redacted connection string]");
        return CredentialPattern.Replace(sanitized, "[redacted credential]");
    }

}
