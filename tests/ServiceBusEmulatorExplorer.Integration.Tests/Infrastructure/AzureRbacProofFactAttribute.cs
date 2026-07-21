namespace ServiceBusEmulatorExplorer.Integration.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AzureRbacProofFactAttribute : FactAttribute
{
    public AzureRbacProofFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBE_RUN_AZURE_RBAC_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set SBE_RUN_AZURE_RBAC_TESTS=true and configure pre-provisioned Azure Service Bus inputs to run Azure RBAC proof tests.";
            return;
        }

        string[] requiredVariables = ["SBE_AZURE_NAMESPACE", "SBE_AZURE_TOPIC", "SBE_AZURE_SUBSCRIPTION"];
        string[] missingVariables = requiredVariables
            .Where(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            .ToArray();

        if (missingVariables.Length > 0)
        {
            Skip = $"Azure RBAC proof requires: {string.Join(", ", missingVariables)}.";
        }
    }
}
