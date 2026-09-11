using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private readonly Dictionary<string, TextDocument> drafts = new(StringComparer.Ordinal);
    private MessageRow? editorRow;
    private bool renderingEditor;

    private void RenderInspector()
    {
        if (BodyEditor is null) return;
        var row = Workspace.FocusedMessage;
        var hasMessage = row is not null;
        EmptyInspector.Visibility = hasMessage ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        InspectorHeading.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        InspectorTabs.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        BodyEditor.Visibility = hasMessage && inspectorMode == "JSON" ? Visibility.Visible : Visibility.Collapsed;
        BodyViewer.Visibility = hasMessage && inspectorMode != "JSON" ? Visibility.Visible : Visibility.Collapsed;
        if (!hasMessage) FindPanel.Visibility = Visibility.Collapsed;
        CopyCorrelationButton.IsEnabled = FindRelatedButton.IsEnabled = !string.IsNullOrEmpty(row?.CorrelationId);
        InspectorState.Background = (Brush)new BrushConverter().ConvertFromString(row?.IsDeadLetter == true ? "#FFE6C8" : "#DBEDFF")!;
        if (InspectorState.Child is System.Windows.Controls.TextBlock state)
            state.Foreground = (Brush)new BrushConverter().ConvertFromString(row?.IsDeadLetter == true ? "#A65B00" : "#0072DD")!;
        if (row != editorRow)
        {
            renderingEditor = true;
            try
            {
                editorRow = row;
                if (row is null) BodyEditor.Document = new TextDocument();
                else
                {
                    if (!drafts.TryGetValue(row.Key, out var draft)) drafts[row.Key] = draft = new TextDocument(JsonPresentation.Format(row.Body));
                    BodyEditor.Document = draft;
                    BodyEditor.IsReadOnly = !row.IsDeadLetter;
                }
                findOffset = 0;
            }
            finally { renderingEditor = false; }
        }
        if (row is not null && inspectorMode != "JSON")
        {
            var text = inspectorMode == "Properties" ? row.Properties : row.Body;
            if (text != renderedBody || renderedMode != inspectorMode)
            {
                renderedBody = text;
                renderedMode = inspectorMode;
                var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 24 };
                if (inspectorMode == "Properties") AddHighlightedJson(paragraph, JsonPresentation.Format(text));
                else paragraph.Inlines.Add(new Run(text));
                BodyViewer.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = JsonPresentation.TextBrush, PageWidth = double.NaN };
            }
        }
        UpdateReplaySurface();
    }

    private bool IsDirty(MessageRow row) => drafts.TryGetValue(row.Key, out var draft) && draft.Text != JsonPresentation.Format(row.Body);

    private void BodyEditor_Changed(object? sender, EventArgs e)
    {
        if (!renderingEditor && IsLoaded) UpdateReplaySurface();
    }

    private void UpdateReplaySurface()
    {
        if (ReplayActions is null) return;
        var row = Workspace.FocusedMessage;
        var dirty = row is not null && IsDirty(row);
        var validJson = true;
        if (row is not null)
        {
            try { using var parsed = JsonDocument.Parse(BodyEditor.Text); }
            catch (JsonException) { validJson = false; }
        }
        JsonTab.Content = validJson ? "JSON" : "Body (not JSON)";
        JsonTab.ToolTip = validJson ? "Formatted JSON" : "Plain text or invalid JSON cannot be formatted; the body is shown as received.";
        ModifiedBadge.Visibility = dirty && inspectorMode == "JSON" ? Visibility.Visible : Visibility.Collapsed;
        DiscardButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        var targets = Workspace.ReplayTargets;
        ReplayActions.Visibility = row is not null && (row.IsDeadLetter || targets.Any(target => target.IsDeadLetter)) ? Visibility.Visible : Visibility.Collapsed;
        ReplayButton.Content = dirty ? "▶  Edit and Replay" : targets.Count > 1 ? $"▶  Replay ({targets.Count})" : "▶  Replay";
        NextReplayIdText.Text = targets.Count > 1 ? "A new ID for each message" : $"Next ID: {Workspace.NextReplayId}";
        NextReplayIdText.ToolTip = NextReplayIdText.Text;
        string? problem = null;
        if (dirty)
        {
            if (!Workspace.CanEditAndReplay) problem = "Select only this message to replay its edited JSON.";
            else
            {
                try { using var parsed = JsonDocument.Parse(BodyEditor.Text); }
                catch (JsonException) { problem = "Complete the JSON before replaying, or discard your changes."; }
            }
        }
        else if (targets.Any(IsDirty)) problem = "A checked message has a draft. Open it to replay or discard its changes.";
        else if (targets.Any(target => !target.IsDeadLetter) && targets.Any(target => target.IsDeadLetter)) problem = "Select only dead-letter messages to replay.";
        ReplayButton.IsEnabled = Workspace.CanReplay && problem is null;
        EditError.Text = problem ?? "";
        EditError.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (editorRow is null) return;
        BodyEditor.Text = JsonPresentation.Format(editorRow.Body);
        BodyEditor.Document.UndoStack.ClearAll();
        UpdateReplaySurface();
    }

    private void ReplayDraft()
    {
        if (!ReplayButton.IsEnabled || Workspace.FocusedMessage is not { } row) return;
        var dirty = IsDirty(row);
        synchronizingSelection = true;
        try
        {
            Workspace.Replay(dirty ? BodyEditor.Text : null);
            if (dirty) Discard_Click(this, new RoutedEventArgs());
        }
        finally { synchronizingSelection = false; }
        SynchronizeSelection();
        UpdateReplaySurface();
        AddLog(Workspace.Status);
    }
}
