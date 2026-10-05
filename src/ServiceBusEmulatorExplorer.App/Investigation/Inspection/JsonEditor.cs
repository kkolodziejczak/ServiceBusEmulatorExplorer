using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>JSON presentation with optional inspection folding; folds never change the document.</summary>
public sealed class JsonEditor : TextEditor
{
    public static readonly RoutedUICommand ToggleJsonFoldCommand = new(
        "Toggle JSON fold", nameof(ToggleJsonFoldCommand), typeof(JsonEditor),
        new InputGestureCollection { new KeyGesture(Key.OemOpenBrackets, ModifierKeys.Control | ModifierKeys.Shift) });
    private FoldingManager? folding;
    private JsonFoldingMargin? foldingMargin;
    private JsonFoldPlaceholder? foldPlaceholder;
    private bool inspectionFolding;

    public JsonEditor()
    {
        WordWrap = true;
        ShowLineNumbers = true;
        Foreground = JsonSyntaxColorizer.TextBrush;
        TextArea.TextView.LineTransformers.Add(new JsonColorizingTransformer());
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.IndentationSize = 2;
        Options.HighlightCurrentLine = true;
        TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(38, 58, 83));
        TextChanged += (_, _) => RefreshFolds();
        Loaded += (_, _) => AttachFolds();
        Unloaded += (_, _) => DetachFolds();
        CommandBindings.Add(new CommandBinding(ToggleJsonFoldCommand,
            (_, e) => { ToggleFoldAtCaret(); e.Handled = true; },
            (_, e) => { e.CanExecute = JsonFoldings.Count > 0; e.Handled = true; }));
    }

    public IReadOnlyList<FoldingSection> JsonFoldings => folding?.AllFoldings.ToArray() ?? [];

    public void SetInspectionDocument(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (inspectionFolding && ReferenceEquals(Document, document)) return;
        DetachFolds();
        inspectionFolding = true;
        Document = document;
        AttachFolds();
    }

    private void AttachFolds()
    {
        if (!inspectionFolding || folding is not null || Document is null) return;
        folding = FoldingManager.Install(TextArea);
        var defaultMargin = TextArea.LeftMargins.OfType<FoldingMargin>().Single();
        int index = TextArea.LeftMargins.IndexOf(defaultMargin);
        TextArea.LeftMargins.Remove(defaultMargin);
        foldingMargin = new JsonFoldingMargin(folding);
        TextArea.LeftMargins.Insert(index, foldingMargin);
        foldPlaceholder = new JsonFoldPlaceholder(folding, this);
        TextArea.TextView.ElementGenerators.Insert(0, foldPlaceholder);
        RefreshFolds();
    }

    private void RefreshFolds()
    {
        folding?.UpdateFoldings(JsonFoldRanges.Create(Text), -1);
        foldingMargin?.Rebuild();
    }

    private void DetachFolds()
    {
        if (folding is null) return;
        if (foldingMargin is not null) TextArea.LeftMargins.Remove(foldingMargin);
        if (foldPlaceholder is not null) TextArea.TextView.ElementGenerators.Remove(foldPlaceholder);
        foldingMargin = null;
        foldPlaceholder = null;
        FoldingManager.Uninstall(folding);
        folding = null;
    }

    public void RevealRange(int start, int length)
    {
        foreach (FoldingSection section in JsonFoldings)
        {
            if (section.StartOffset < start + length && section.EndOffset > start)
                section.IsFolded = false;
        }
    }

    public void ToggleFoldAtCaret()
    {
        FoldingSection? section = JsonFoldings
            .Where(item => item.StartOffset - 1 <= CaretOffset && item.EndOffset >= CaretOffset)
            .OrderBy(item => item.EndOffset - item.StartOffset)
            .FirstOrDefault();
        if (section is not null) section.IsFolded = !section.IsFolded;
    }

}
