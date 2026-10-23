using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Core.ServiceBus;

namespace ServiceBusEmulatorExplorer.Avalonia.Investigation;

/// <summary>The dead-letter deliveries the WPF Delete dialog baselines use: very long names that must stay out of the summary.</summary>
public static class SampleDeliveries
{
    public static MessageDelivery[] DeadLetters(int count) => Enumerable.Range(1, count).Select(Delivery).ToArray();

    private static MessageDelivery Delivery(int sequence)
    {
        string longText = new('W', 300);
        var message = new ExplorerMessage(longText, sequence, longText, longText, 300, null, null, 0, "text/plain", null, null, null,
            new Dictionary<string, object?>(), new Dictionary<string, object?>());
        return new(new(1, new(EntityKind.Queue, longText + sequence), MessageBucket.DeadLetter, sequence), message);
    }
}
