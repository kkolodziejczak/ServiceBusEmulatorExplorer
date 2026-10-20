using System.Runtime.CompilerServices;
using ICSharpCode.AvalonEdit.Document;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>
/// Gives each inspection document one AvalonEdit document for the editor to show. Edits flow both ways, and the
/// AvalonEdit document keeps the undo history for as long as the inspection document exists.
/// </summary>
public static class EditorDocuments
{
    private static readonly ConditionalWeakTable<InspectionDocument, TextDocument> Documents = new();

    public static TextDocument For(InspectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Documents.GetValue(document, Create);
    }

    private static TextDocument Create(InspectionDocument source)
    {
        var editor = new TextDocument(source.Text);
        editor.TextChanged += (_, _) => source.Text = editor.Text;
        source.TextChanged += (_, _) =>
        {
            if (!string.Equals(editor.Text, source.Text, StringComparison.Ordinal))
                editor.Replace(0, editor.TextLength, source.Text);
        };
        source.HistoryReset += (_, _) => editor.UndoStack.ClearAll();
        return editor;
    }
}
