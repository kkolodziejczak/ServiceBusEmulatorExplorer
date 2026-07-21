using ServiceBusEmulatorExplorer.Core.Connection;

namespace ServiceBusEmulatorExplorer.Core.Tests.Connection;

public sealed class ConnectionProfileValidatorTests
{
    [Fact]
    public void Validate_accepts_default_local_emulator_profile()
    {
        ValidationResult result = ConnectionProfileValidator.Validate(ConnectionProfileDefaults.LocalEmulator);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_rejects_missing_profile_fields()
    {
        var profile = new ConnectionProfile("", "", "");

        ValidationResult result = ConnectionProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains("Profile name is required.", result.Errors);
        Assert.Contains("Runtime connection string is required.", result.Errors);
        Assert.Contains("Administration connection string is required.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_connection_strings_without_service_bus_endpoint()
    {
        var profile = new ConnectionProfile("Local", "UseDevelopmentEmulator=true;", "UseDevelopmentEmulator=true;");

        ValidationResult result = ConnectionProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains("Runtime connection string must include an Endpoint=sb:// value.", result.Errors);
        Assert.Contains("Administration connection string must include an Endpoint=sb:// value.", result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://orders.servicebus.windows.net")]
    [InlineData("orders.servicebus.windows.net/path")]
    [InlineData("Endpoint=sb://orders.servicebus.windows.net;")]
    [InlineData("orders.servicebus.windows.net:443")]
    public void Validate_rejects_invalid_azure_cli_namespace(string fullyQualifiedNamespace)
    {
        var profile = new ConnectionProfile(
            "Azure",
            "ignored",
            "ignored",
            ConnectionAuthenticationMode.AzureCli,
            fullyQualifiedNamespace);

        ValidationResult result = ConnectionProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_accepts_trimmed_azure_cli_namespace_and_ignores_connection_strings()
    {
        var profile = new ConnectionProfile(
            "Azure",
            "not a connection string",
            "not a connection string",
            ConnectionAuthenticationMode.AzureCli,
            " orders.servicebus.windows.net ");

        ValidationResult result = ConnectionProfileValidator.Validate(profile);

        Assert.True(result.IsValid);
    }
}
