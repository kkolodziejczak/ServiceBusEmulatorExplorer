namespace ServiceBusEmulatorExplorer.Core.Messaging;

public static class ReplayMessageIdFactory
{
    public static string Create(ReplayIdPolicy policy, string originalMessageId, string? manualMessageId = null)
    {
        return policy switch
        {
            ReplayIdPolicy.NewGuid => Guid.NewGuid().ToString("N"),
            ReplayIdPolicy.PrefixOriginalId => $"replay-{originalMessageId}-{Guid.NewGuid():N}",
            ReplayIdPolicy.Manual => CreateManualMessageId(manualMessageId),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported replay ID policy.")
        };
    }

    private static string CreateManualMessageId(string? manualMessageId)
    {
        return !string.IsNullOrWhiteSpace(manualMessageId)
            ? manualMessageId
            : throw new ArgumentException("Manual replay message ID is required.", nameof(manualMessageId));
    }
}
