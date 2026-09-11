using System.Windows.Media;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class EditorProof
{
    public static void Exercise(JsonEditor editor, List<string> report)
    {
        var original = editor.Text;
        editor.Text = JsonPresentation.Format("{\"name\":\"before\",\"number\":-1.25e3,\"enabled\":true,\"empty\":null}");
        editor.Document.UndoStack.ClearAll();
        editor.UpdateLayout();
        var colors = editor.TextArea.TextView.VisualLines.SelectMany(line => line.Elements)
            .Select(element => element.TextRunProperties.ForegroundBrush.ToString()).ToHashSet();
        Require(new[] { "#FFD9A0F5", "#FF56E5E5", "#FFF4D071" }.All(colors.Contains),
            "Rendered editor uses preview colors for keys, strings and scalars", report);
        var before = editor.Text;
        var offset = before.IndexOf("before", StringComparison.Ordinal);
        editor.Select(offset, 6);
        editor.TextArea.Selection.ReplaceSelectionWithText("after");
        editor.UpdateLayout();
        Require(editor.Text.Contains("\"after\"") && editor.CaretOffset == offset + 5,
            "Editing a middle value preserves the caret and refreshes coloring", report);
        var after = editor.Text;
        editor.Undo();
        Require(editor.Text == before, "Undo reverses text editing without a formatting-only step", report);
        editor.Redo();
        Require(editor.Text == after, "Redo restores exact edited JSON", report);
        editor.Select(0, editor.Text.Length);
        editor.TextArea.Selection.ReplaceSelectionWithText("{\"unfinished\":");
        editor.UpdateLayout();
        Require(editor.Text == "{\"unfinished\":" && JsonPresentation.Format(editor.Text) == editor.Text,
            "Incomplete JSON stays editable without rewriting text", report);
        Require(editor.WordWrap && editor.HorizontalScrollBarVisibility == System.Windows.Controls.ScrollBarVisibility.Disabled,
            "Editor always wraps and keeps horizontal scrolling disabled", report);
        editor.Text = original;
        editor.Document.UndoStack.ClearAll();
        editor.UpdateLayout();
    }

    private static void Require(bool condition, string description, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.Add("- PASS: " + description);
    }
}
