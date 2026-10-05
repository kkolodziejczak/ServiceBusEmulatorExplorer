using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>Presentation-only gutter; the existing manager remains the owner of fold state.</summary>
public sealed class JsonFoldingMargin : AbstractMargin
{
    private readonly FoldingManager manager;
    private readonly Canvas canvas = new() { ClipToBounds = true };
    private readonly Dictionary<FoldingSection, Button> buttons = [];

    public JsonFoldingMargin(FoldingManager manager)
    {
        this.manager = manager;
        AddVisualChild(canvas);
        AddLogicalChild(canvas);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? canvas : throw new ArgumentOutOfRangeException(nameof(index));
    protected override Size MeasureOverride(Size availableSize)
    {
        canvas.Measure(new Size(24, availableSize.Height));
        return new Size(24, 0);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        canvas.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= VisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += VisualLinesChanged;
        Rebuild();
    }

    private void VisualLinesChanged(object? sender, EventArgs e) => Rebuild();

    internal void Rebuild()
    {
        if (TextView is null)
        {
            canvas.Children.Clear();
            buttons.Clear();
            return;
        }
        if (!TextView.VisualLinesValid) return;
        var visible = new HashSet<FoldingSection>();
        foreach (var line in TextView.VisualLines)
        {
            var fold = manager.GetNextFolding(line.FirstDocumentLine.Offset);
            if (fold is null || fold.StartOffset > line.FirstDocumentLine.EndOffset) continue;
            visible.Add(fold);
            if (!buttons.TryGetValue(fold, out var button))
            {
                button = CreateButton(fold);
                buttons.Add(fold, button);
                canvas.Children.Add(button);
            }
            var glyph = (Path)button.Content;
            glyph.SetResourceReference(Path.DataProperty, fold.IsFolded ? "ChevronRightGeometry" : "ChevronDownGeometry");
            glyph.Width = fold.IsFolded ? 5 : 8;
            glyph.Height = fold.IsFolded ? 10 : 4;
            string action = $"{(fold.IsFolded ? "Expand" : "Collapse")} JSON at line {line.FirstDocumentLine.LineNumber}";
            button.ToolTip = action;
            AutomationProperties.SetName(button, action);
            double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextMiddle) - TextView.VerticalOffset;
            Canvas.SetLeft(button, 2);
            Canvas.SetTop(button, y - button.Height / 2);
        }
        foreach (var fold in buttons.Keys.Where(fold => !visible.Contains(fold)).ToArray())
        {
            canvas.Children.Remove(buttons[fold]);
            buttons.Remove(fold);
        }
    }

    private Button CreateButton(FoldingSection fold)
    {
        var glyph = new Path { Stretch = Stretch.Fill, StrokeThickness = 1.5 };
        glyph.SetResourceReference(Shape.StrokeProperty, "EditorForegroundBrush");
        var button = new Button { Width = 20, Height = 16, Content = glyph };
        button.SetResourceReference(StyleProperty, "JsonFoldButton");
        button.Click += (_, _) =>
        {
            fold.IsFolded = !fold.IsFolded;
            Rebuild();
        };
        return button;
    }
}
