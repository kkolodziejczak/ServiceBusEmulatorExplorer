namespace ServiceBusEmulatorExplorer.App.Services;

public sealed record EntityManagementOperationResult(bool Changed, string? LogMessage)
{
    public static EntityManagementOperationResult NoChange { get; } = new(false, null);

    public static EntityManagementOperationResult ChangedWithLog(string logMessage)
    {
        return new EntityManagementOperationResult(true, logMessage);
    }
}
