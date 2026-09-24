using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
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
    [InlineData(ConnectionAuthenticationMode.AzureCli, "Data Owner at namespace scope")]
    [InlineData(ConnectionAuthenticationMode.ConnectionString, "connection string")]
    public void Format_maps_unauthorized_access_to_mode_appropriate_guidance(
        ConnectionAuthenticationMode authenticationMode,
        string expectedGuidance)
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new UnauthorizedAccessException("authorization details must not be logged"),
            authenticationMode);

        Assert.Contains(expectedGuidance, failure.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization details", failure.Detail, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Format_includes_sanitized_service_bus_exception_and_inner_connectivity_failure()
    {
        var exception = new ServiceBusException(
            "Unable to connect to Endpoint=sb://localhost:5300;SharedAccessKey=secret-value;Bearer bearer-value",
            ServiceBusFailureReason.GeneralError,
            "orders",
            new TimeoutException("The connection timed out while opening the AMQP link."));

        OperationFailure failure = OperationFailureFormatter.Format(exception, ConnectionAuthenticationMode.ConnectionString);

        Assert.Equal("The operation failed.", failure.UserMessage);
        Assert.Contains("ServiceBusException", failure.Detail, StringComparison.Ordinal);
        Assert.Contains("Unable to connect", failure.Detail, StringComparison.Ordinal);
        Assert.Contains("TimeoutException: The connection timed out while opening the AMQP link.", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedAccessKey=secret-value", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-value", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_redacts_credential_like_values_outside_connection_strings()
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new TimeoutException("Connection failed: SharedAccessKey=secret-key, Bearer bearer-value, access_token=token-value, Password=\"quoted-secret\", sig=signature-value"),
            ConnectionAuthenticationMode.ConnectionString);

        Assert.Contains("TimeoutException: Connection failed", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-value", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("quoted-secret", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("signature-value", failure.Detail, StringComparison.Ordinal);
        Assert.Contains("[redacted credential]", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_redacts_json_credentials_in_a_logged_connectivity_exception()
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new TimeoutException("Broker response: {\"access_token\":\"json-secret\",\"client_secret\":\"client-secret\",\"SharedAccessKey\":\"sas-secret\"}"),
            ConnectionAuthenticationMode.ConnectionString);

        Assert.Contains("TimeoutException: Broker response", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("client-secret", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("sas-secret", failure.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("refresh_token")]
    [InlineData("refreshToken")]
    [InlineData("id_token")]
    [InlineData("sas_token")]
    [InlineData("shared_access_key")]
    [InlineData("x-api-key")]
    public void Format_redacts_credential_key_aliases_in_connectivity_exceptions(string key)
    {
        OperationFailure failure = OperationFailureFormatter.Format(
            new TimeoutException($"Broker response: {key}=secret-value"),
            ConnectionAuthenticationMode.ConnectionString);

        Assert.Contains("TimeoutException: Broker response", failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", failure.Detail, StringComparison.Ordinal);
    }
}
