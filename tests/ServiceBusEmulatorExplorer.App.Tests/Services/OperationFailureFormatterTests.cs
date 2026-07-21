using Azure;
using Azure.Identity;
using ServiceBusEmulatorExplorer.App.Services;
using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.App.Tests.Services;

public sealed class OperationFailureFormatterTests
{
    [Fact]
    public void Format_maps_credential_unavailable_to_azure_cli_guidance()
    {
        OperationFailure failure = OperationFailureFormatter.Format(new CredentialUnavailableException("Azure CLI was not found."), ConnectionAuthenticationMode.AzureCli);

        Assert.Equal("Azure CLI authentication is unavailable. Run az login, then reconnect.", failure.UserMessage);
        Assert.Equal("Credential details are omitted from the operation log.", failure.Detail);
    }

    [Fact]
    public void Format_maps_403_to_authorization_guidance_without_matching_exception_message()
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new RequestFailedException(403, "Localized server detail", "AuthorizationFailed", null),
            ConnectionAuthenticationMode.AzureCli);

        Assert.Contains("permission", failure.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("HTTP 403 (AuthorizationFailed).", failure.Detail);
    }

    [Fact]
    public void Format_maps_connection_string_403_to_sas_guidance()
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new RequestFailedException(403, "Localized server detail", "AuthorizationFailed", null),
            ConnectionAuthenticationMode.ConnectionString);

        Assert.Contains("connection string", failure.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure CLI", failure.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SharedAccessKey=secret-value")]
    [InlineData("access_token=secret-value")]
    [InlineData("Bearer secret-value")]
    [InlineData("{\"access_token\":\"secret-value\"}")]
    [InlineData("{\"accessToken\":\"secret-value\"}")]
    [InlineData("access_token: secret-value")]
    public void Format_omits_generic_exception_details_that_may_contain_credentials(string secretDetail)
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new InvalidOperationException(secretDetail),
            ConnectionAuthenticationMode.AzureCli);

        Assert.DoesNotContain("secret-value", failure.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", failure.Detail, StringComparison.Ordinal);
        Assert.Equal("The operation failed.", failure.UserMessage);
        Assert.Contains("raw exception details are omitted", failure.Detail, StringComparison.Ordinal);
    }
}
