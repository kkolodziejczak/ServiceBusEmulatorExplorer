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
}
