using System.Text.RegularExpressions;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>An application property row in Compose; a new row starts empty as a string.</summary>
public sealed class PrototypeProperty(string name, string type, string value)
{
    public PrototypeProperty() : this("", "string", "") { }

    /// <summary>Whether the row is still blank, so it is dropped rather than saved.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Value) && Type == "string";
    public string Name { get; set; } = name;
    public string Type { get; set; } = type;
    public string Value { get; set; } = value;
}

/// <summary>
/// A template variable row in Compose. Inputs may declare a default; generated variables show where their value comes
/// from instead.
/// </summary>
public sealed class PrototypeVariable(string name, string type, string source, string defaultValue)
{
    public string Name { get; set; } = name;
    public string Type { get; set; } = type;
    public string Source { get; set; } = source;
    public bool HasDefault { get; set; }
    public string DefaultValue { get; set; } = "";

    /// <summary>The default as the list shows it: the declared value, "none (required)", or the generated source's text.</summary>
    public string Default => Source == "Input" ? (HasDefault ? DefaultValue : "none (required)") : defaultValue;

    /// <summary>Whether <paramref name="name"/> can name a variable: letters, digits or underscores, not starting with a digit.</summary>
    public static bool IsValidName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$");
}
