using System.Windows;
using System.Windows.Controls;

namespace ServiceBusEmulatorExplorer.App.Investigation;

/// <summary>
/// An action row (Results 71): children stay in order, each line is right-aligned, and when they do not fit the last ones
/// wrap onto the next line instead of being clipped, so the primary action placed last stays last.
/// </summary>
public sealed class RightAlignedWrapPanel : Panel
{
    public static readonly DependencyProperty LineSpacingProperty = DependencyProperty.Register(
        nameof(LineSpacing), typeof(double), typeof(RightAlignedWrapPanel),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The gap between wrapped lines.</summary>
    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var lines = Lines(availableSize.Width);
        return new Size(lines.Count == 0 ? 0 : lines.Max(line => line.Width),
            lines.Sum(line => line.Height) + LineSpacing * Math.Max(0, lines.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = 0;
        foreach (var line in Lines(finalSize.Width))
        {
            double left = finalSize.Width - line.Width;
            foreach (UIElement child in line.Children)
            {
                child.Arrange(new Rect(left, top, child.DesiredSize.Width, line.Height));
                left += child.DesiredSize.Width;
            }
            top += line.Height + LineSpacing;
        }
        return finalSize;
    }

    private List<Line> Lines(double width)
    {
        var lines = new List<Line>();
        Line? current = null;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (current is null || (current.Children.Count > 0 && current.Width + child.DesiredSize.Width > width))
                lines.Add(current = new Line());
            current.Children.Add(child);
            current.Width += child.DesiredSize.Width;
            current.Height = Math.Max(current.Height, child.DesiredSize.Height);
        }
        return lines;
    }

    private sealed class Line
    {
        public List<UIElement> Children { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
