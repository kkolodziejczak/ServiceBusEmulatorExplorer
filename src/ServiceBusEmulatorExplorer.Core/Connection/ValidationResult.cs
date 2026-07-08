namespace ServiceBusEmulatorExplorer.Core.Connection;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success { get; } = new(true, []);
}
