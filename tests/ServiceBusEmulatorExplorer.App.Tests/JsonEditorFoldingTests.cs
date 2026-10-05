using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ServiceBusEmulatorExplorer.App.Investigation.Inspection;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class JsonEditorFoldingTests
{
    private const string MultilineJson = """
        {
          "quoted": "braces { [ } ] and an escaped quote \" still inside",
          "nested": [
            {
              "value": "needle"
            }
          ]
        }
        """;

    [Fact]
    public void Valid_multiline_json_folds_objects_and_arrays_without_changing_document_or_undo()
    {
        OnSta(() =>
        {
            var editor = new JsonEditor();
            var document = new TextDocument(MultilineJson);
            editor.SetInspectionDocument(document);

            Assert.Same(document, editor.Document);
            Assert.Equal(MultilineJson, document.Text);
            Assert.False(document.UndoStack.CanUndo);
            Assert.Equal(3, editor.JsonFoldings.Count);
        });
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Multiline_folds_accept_all_document_line_endings(string newline) => OnSta(() =>
    {
        var editor = new JsonEditor();
        var text = "{" + newline + "  \"items\": [" + newline + "    1" + newline + "  ]" + newline + "}";
        editor.SetInspectionDocument(new TextDocument(text));
        Assert.Equal(2, editor.JsonFoldings.Count);
        Assert.Equal(text, editor.Text);
    });

    [Fact]
    public void Invalid_json_clears_old_folds_and_find_reveals_a_folded_match()
    {
        OnSta(() =>
        {
            var editor = new JsonEditor();
            var document = new TextDocument(MultilineJson);
            editor.SetInspectionDocument(document);
            Assert.Equal(3, editor.JsonFoldings.Count);

            FoldingSection[] folds = editor.JsonFoldings.ToArray();
            foreach (FoldingSection fold in folds) fold.IsFolded = true;
            int matchStart = MultilineJson.IndexOf("needle", StringComparison.Ordinal);
            editor.RevealRange(matchStart, "needle".Length);
            Assert.All(folds, fold => Assert.False(fold.IsFolded));

            document.Text = "{\n  \"nested\": [\n";
            Assert.Empty(editor.JsonFoldings);
            Assert.Equal("{\n  \"nested\": [\n", document.Text);
        });
    }

    [Fact]
    public void Switching_documents_and_unloading_detaches_the_folding_manager()
    {
        OnSta(() =>
        {
            var editor = new JsonEditor();
            var first = new TextDocument(MultilineJson);
            editor.SetInspectionDocument(first);
            Assert.Equal(3, editor.JsonFoldings.Count);

            const string replacementJson = "{\n  \"replacement\": [\n    1\n  ]\n}";
            var replacement = new TextDocument(replacementJson);
            editor.SetInspectionDocument(replacement);
            Assert.Same(replacement, editor.Document);
            Assert.Equal(2, editor.JsonFoldings.Count);

            first.Text = "not valid JSON";
            Assert.Equal(2, editor.JsonFoldings.Count);

            var window = new Window { Content = editor, Width = 400, Height = 300, ShowInTaskbar = false };
            try
            {
                window.Show();
                Assert.IsAssignableFrom<FoldingManager>(editor.TextArea.GetService(typeof(FoldingManager)));
                window.Close();
                editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Null(editor.TextArea.GetService(typeof(FoldingManager)));
                Assert.Empty(editor.JsonFoldings);

                replacement.Text = MultilineJson;
                Assert.Empty(editor.JsonFoldings);
            }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        });
    }

    [Fact]
    public void Toggle_fold_at_caret_changes_the_containing_multiline_section()
    {
        OnSta(() =>
        {
            var editor = new JsonEditor();
            editor.SetInspectionDocument(new TextDocument(MultilineJson));
            editor.CaretOffset = MultilineJson.IndexOf("\"quoted\"", StringComparison.Ordinal);

            editor.ToggleFoldAtCaret();
            Assert.Contains(editor.JsonFoldings, fold => fold.IsFolded);

            editor.ToggleFoldAtCaret();
            Assert.DoesNotContain(editor.JsonFoldings, fold => fold.IsFolded);
        });
    }

    [Fact]
    public void Routed_fold_command_uses_the_ctrl_shift_open_bracket_gesture()
    {
        OnSta(() =>
        {
            var editor = new JsonEditor();
            editor.SetInspectionDocument(new TextDocument(MultilineJson));
            editor.CaretOffset = MultilineJson.IndexOf("\"quoted\"", StringComparison.Ordinal);
            KeyGesture gesture = Assert.Single(JsonEditor.ToggleJsonFoldCommand.InputGestures.OfType<KeyGesture>());

            Assert.Equal(Key.OemOpenBrackets, gesture.Key);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, gesture.Modifiers);
            Assert.True(JsonEditor.ToggleJsonFoldCommand.CanExecute(null, editor));
            JsonEditor.ToggleJsonFoldCommand.Execute(null, editor);
            Assert.Contains(editor.JsonFoldings, fold => fold.IsFolded);
        });
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The JSON folding proof exceeded its 15-second bound.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
