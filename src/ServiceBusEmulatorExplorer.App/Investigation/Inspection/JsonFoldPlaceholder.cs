using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace ServiceBusEmulatorExplorer.App.Investigation.Inspection;

/// <summary>Instance-scoped folded text styling, without AvalonEdit's system-colored outline.</summary>
internal sealed class JsonFoldPlaceholder(FoldingManager manager, JsonEditor editor) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset) => manager.GetNextFoldedFoldingStart(startOffset);

    public override VisualLineElement? ConstructElement(int offset)
    {
        var fold = manager.GetFoldingsContaining(offset).Where(section => section.IsFolded)
            .OrderByDescending(section => section.EndOffset).FirstOrDefault();
        return fold is null ? null : new FoldElement(fold, offset, editor);
    }

    private sealed class FoldElement(FoldingSection fold, int offset, JsonEditor editor)
        : FormattedTextElement(" … ", fold.EndOffset - offset)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            TextRunProperties.SetForegroundBrush(editor.TryFindResource("EditorForegroundBrush") as Brush ?? editor.Foreground);
            // The base call prepares the TextLine consumed by FormattedTextRun.Format/Draw.
            _ = base.CreateTextRun(startVisualColumn, context);
            return new FoldRun(this, TextRunProperties, editor);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                fold.IsFolded = false;
                e.Handled = true;
            }
            else base.OnMouseDown(e);
        }
    }

    private sealed class FoldRun(FormattedTextElement element, TextRunProperties properties, JsonEditor editor)
        : FormattedTextRun(element, properties)
    {
        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
        {
            var metrics = Format(double.PositiveInfinity);
            var bounds = new Rect(origin.X, origin.Y - metrics.Baseline, metrics.Width, metrics.Height);
            drawingContext.DrawRoundedRectangle(editor.TryFindResource("DarkButtonBrush") as Brush,
                null, bounds, 3, 3);
            base.Draw(drawingContext, origin, rightToLeft, sideways);
        }
    }
}
