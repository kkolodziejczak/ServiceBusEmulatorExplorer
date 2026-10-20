namespace ServiceBusEmulatorExplorer.Presentation.Investigation.Inspection;

/// <summary>The inspector shows the message body or its properties.</summary>
public enum InspectorMode
{
    Body,
    Properties
}

/// <summary>
/// The message inspector beside the list: whether it shows the body or the properties, whether the body can be
/// edited and why not, which badges show, and copying what it shows.
/// </summary>
public sealed class InspectorPanelViewModel
{
    private readonly InvestigationWorkspace workspace;
    private readonly Action<string> copyText;

    /// <param name="copyText">Puts text on the clipboard; it may throw when the clipboard cannot be used.</param>
    public InspectorPanelViewModel(InvestigationWorkspace workspace, Action<string> copyText)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(copyText);
        this.workspace = workspace;
        this.copyText = copyText;
    }

    public InspectorMode Mode { get; private set; } = InspectorMode.Body;

    /// <summary>Switches between body and properties; returns <c>false</c> when the mode was already shown.</summary>
    public bool SetMode(InspectorMode mode)
    {
        if (Mode == mode) return false;
        Mode = mode;
        return true;
    }

    /// <param name="replayPending">A replay is being reviewed or sent.</param>
    public InspectorPanelState StateFor(bool replayPending)
    {
        var inspector = workspace.Inspector;
        var focused = workspace.Surface.FocusedMessage;
        bool hasMessage = focused is not null && inspector.Current is not null;
        bool readOnly = inspector.IsReadOnly || replayPending;
        bool body = Mode == InspectorMode.Body;
        bool editable = body && !readOnly;
        string help = editable
            ? "Edit this body, then replay a new copy. Original stays unchanged."
            : replayPending ? "Read-only while replay is in progress."
            : !body ? "Message properties are read-only. You can select and copy them."
            : "This message body is read-only. You can select, find, fold and copy its content.";
        string jsonTabToolTip = inspector.IsValidJson
            ? "Formatted JSON"
            : inspector.BodyDescription.Contains("Base64", StringComparison.Ordinal) ? inspector.BodyDescription
            : "Plain text or invalid JSON cannot be formatted; the body is shown as received.";
        return new(hasMessage, focused?.IsDeadLetter == true, readOnly, editable, editable ? "Editable" : "Read-only", help,
            hasMessage && body, hasMessage && !body, jsonTabToolTip, hasMessage && inspector.IsDirty);
    }

    /// <summary>Copies the shown body or properties of the inspected message.</summary>
    public void CopyShown()
    {
        if (workspace.Inspector.Current is null) return;
        Copy(Mode == InspectorMode.Properties ? workspace.Inspector.PropertiesText : workspace.Inspector.Document.Text);
    }

    /// <summary>Copies the correlation ID of <paramref name="row"/> when it has one.</summary>
    public void CopyCorrelation(MessageRow? row)
    {
        if (row is { CorrelationId.Length: > 0 }) Copy(row.CorrelationId);
    }

    /// <summary>Copies text and logs whether the clipboard took it.</summary>
    public void Copy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            copyText(text);
            workspace.Log("Copied displayed text to clipboard.");
        }
        catch (Exception)
        {
            workspace.Log("Clipboard could not be accessed. Try Copy again.", true);
        }
    }
}

/// <summary>
/// The inspector: whether a message is inspected, whether it is dead-lettered (its badge and reason), whether the body
/// is read-only or editable with its status label and help, whether the body or the properties show, the JSON tab
/// tooltip, and whether the Modified badge and Discard show.
/// </summary>
public sealed record InspectorPanelState(bool HasMessage, bool IsDeadLetter, bool IsReadOnly, bool IsEditable,
    string Editability, string EditabilityHelp, bool ShowsBody, bool ShowsProperties, string JsonTabToolTip, bool IsModified);
