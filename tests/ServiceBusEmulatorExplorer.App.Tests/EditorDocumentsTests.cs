using ServiceBusEmulatorExplorer.App.Investigation.Inspection;
using ServiceBusEmulatorExplorer.Presentation.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class EditorDocumentsTests
{
    [Fact]
    public void Each_inspection_document_has_one_editor_document_with_its_text()
    {
        var first = new InspectionDocument("{\"first\":true}");
        var second = new InspectionDocument("{\"first\":true}");

        Assert.Same(EditorDocuments.For(first), EditorDocuments.For(first));
        Assert.NotSame(EditorDocuments.For(first), EditorDocuments.For(second));
        Assert.Equal("{\"first\":true}", EditorDocuments.For(first).Text);
        Assert.Throws<ArgumentNullException>(() => EditorDocuments.For(null!));
    }

    [Fact]
    public void Typing_in_the_editor_changes_the_inspection_document_once()
    {
        var document = new InspectionDocument("{}");
        var editor = EditorDocuments.For(document);
        var announced = new List<string>();
        int editorChanges = 0;
        document.TextChanged += (_, _) => announced.Add(document.Text);
        editor.Changed += (_, _) => editorChanges++;

        editor.Insert(1, "\"a\":1");
        editor.UndoStack.Undo();

        Assert.Equal(["{\"a\":1}", "{}"], announced);
        Assert.Equal(2, editorChanges);
    }

    [Fact]
    public void Text_set_on_the_inspection_document_reaches_the_editor_and_can_be_undone()
    {
        var document = new InspectionDocument("{}");
        var editor = EditorDocuments.For(document);

        document.Text = "{\"edited\":true}";

        Assert.Equal("{\"edited\":true}", editor.Text);
        Assert.True(editor.UndoStack.CanUndo);
        editor.UndoStack.Undo();
        Assert.Equal("{}", document.Text);
    }

    [Fact]
    public void Reset_puts_the_text_back_in_the_editor_and_forgets_its_undo_history()
    {
        var document = new InspectionDocument("{}");
        var editor = EditorDocuments.For(document);
        editor.Insert(1, "\"a\":1");

        document.Reset("{}");

        Assert.Equal("{}", editor.Text);
        Assert.False(editor.UndoStack.CanUndo);
    }
}
