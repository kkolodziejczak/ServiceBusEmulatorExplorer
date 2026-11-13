namespace ServiceBusWorkbench.Integration.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AzureRbacProofFactAttribute : FactAttribute
{
    public AzureRbacProofFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SBW_RUN_AZURE_RBAC_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set SBW_RUN_AZURE_RBAC_TESTS=true and configure pre-provisioned Azure Service Bus inputs to run Azure RBAC proof tests.";
            return;
        }

        string[] requiredVariables = ["SBW_AZURE_NAMESPACE", "SBW_AZURE_TOPIC", "SBW_AZURE_SUBSCRIPTION"];
        string[] missingVariables = requiredVariables
            .Where(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            .ToArray();

        if (missingVariables.Length > 0)
        {
            Skip = $"Azure RBAC proof requires: {string.Join(", ", missingVariables)}.";
        }
    }
}
