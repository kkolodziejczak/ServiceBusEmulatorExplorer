namespace ServiceBusEmulatorExplorer.Core.Connection;

public static class ConnectionProfileValidator
{
    public static ValidationResult Validate(ConnectionProfile profile)
    {
        List<string> errors = [];

        AddRequiredError(errors, profile.Name, "Profile name");
        if (profile.AuthenticationMode == ConnectionAuthenticationMode.ConnectionString)
        {
            AddRequiredError(errors, profile.RuntimeConnectionString, "Runtime connection string");
            AddRequiredError(errors, profile.AdministrationConnectionString, "Administration connection string");
            AddEndpointError(errors, profile.RuntimeConnectionString, "Runtime connection string");
            AddEndpointError(errors, profile.AdministrationConnectionString, "Administration connection string");
        }
        else if (profile.AuthenticationMode == ConnectionAuthenticationMode.AzureCli)
        {
            AddFullyQualifiedNamespaceError(errors, profile.FullyQualifiedNamespace);
        }
        else
        {
            errors.Add("Authentication mode is not supported.");
        }

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

    private static void AddFullyQualifiedNamespaceError(List<string> errors, string value)
    {
        string namespaceHost = value?.Trim() ?? "";
        if (namespaceHost.Length == 0)
        {
            errors.Add("Fully qualified namespace is required.");
            return;
        }

        bool isHostname = Uri.CheckHostName(namespaceHost) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
        if (!isHostname || namespaceHost.Contains("://", StringComparison.Ordinal) ||
            namespaceHost.Contains('/') || namespaceHost.Contains('?') || namespaceHost.Contains('#') ||
            namespaceHost.Contains(':') || namespaceHost.Contains(';') || namespaceHost.Any(char.IsWhiteSpace))
        {
            errors.Add("Fully qualified namespace must be a hostname without a scheme, path, query, fragment, or port.");
        }
    }
}
