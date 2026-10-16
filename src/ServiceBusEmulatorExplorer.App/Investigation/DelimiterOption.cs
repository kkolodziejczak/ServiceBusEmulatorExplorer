namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>CSV delimiter choice shown as a key-cap character followed by its name (Delimiter 67, DEC-047).</summary>
public sealed class DelimiterOption
{
    public string Character { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // UI Automation and keyboard type-ahead read the name, not the type.
    public override string ToString() => Name;
}
