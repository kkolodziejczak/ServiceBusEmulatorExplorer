namespace ServiceBusEmulatorExplorer.Core.Connection;

public static class ConnectionProfileValidator
{
    public static ValidationResult Validate(ConnectionProfile profile)
    {
        List<string> errors = [];

        AddRequiredError(errors, profile.Name, "Profile name");
        AddRequiredError(errors, profile.RuntimeConnectionString, "Runtime connection string");
        AddRequiredError(errors, profile.AdministrationConnectionString, "Administration connection string");
        AddEndpointError(errors, profile.RuntimeConnectionString, "Runtime connection string");
        AddEndpointError(errors, profile.AdministrationConnectionString, "Administration connection string");

        return errors.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(false, errors);
    }

    private static void AddRequiredError(List<string> errors, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} is required.");
        }
    }

    private static void AddEndpointError(List<string> errors, string value, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !value.Contains("Endpoint=sb://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{fieldName} must include an Endpoint=sb:// value.");
        }
    }
}
