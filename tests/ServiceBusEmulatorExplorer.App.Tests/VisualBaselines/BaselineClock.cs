namespace ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;

/// <summary>A clock frozen at one instant, so timestamps in baseline renders never change.</summary>
internal sealed class BaselineClock : TimeProvider
{
    public static readonly BaselineClock Instance = new();

    public override DateTimeOffset GetUtcNow() => new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Returns a fresh source of distinct, repeatable IDs (…0001, …0002, …) for generated replay IDs.</summary>
    public static Func<Guid> ReplayIds()
    {
        int next = 0;
        return () => new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)Interlocked.Increment(ref next));
    }
}
