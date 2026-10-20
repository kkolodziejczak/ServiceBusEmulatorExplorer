namespace ServiceBusEmulatorExplorer.Presentation.Investigation;

/// <summary>
/// The editable text of one inspected delivery. The text editor that shows it keeps its own undo history and
/// drops that history when <see cref="Reset"/> puts the text back.
/// </summary>
public sealed class InspectionDocument
{
    private string text;

    public InspectionDocument(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        this.text = text;
    }

    public string Text
    {
        get => text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (string.Equals(text, value, StringComparison.Ordinal)) return;
            text = value;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? TextChanged;

    /// <summary>Raised by <see cref="Reset"/> after the text is put back; editors forget earlier edits.</summary>
    public event EventHandler? HistoryReset;

    /// <summary>Replaces the text so that earlier edits can no longer be undone.</summary>
    public void Reset(string replacement)
    {
        Text = replacement;
        HistoryReset?.Invoke(this, EventArgs.Empty);
    }
}
