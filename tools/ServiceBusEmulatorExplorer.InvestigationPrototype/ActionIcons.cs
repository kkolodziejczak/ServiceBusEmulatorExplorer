using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

public partial class PrototypeWindow
{
    private const string PlayIcon = "M3,1 L15,8 L3,15 Z";
    private const string PauseIcon = "M4,1 V15 M12,1 V15";

    private static void SetIconAction(Button button, string geometry, string label, bool showLabel = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry), Width = 16, Height = 16,
            Stretch = Stretch.Uniform, StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, showLabel ? 7 : 0, 0)
        };
        icon.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(icon);
        if (showLabel) content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        AutomationProperties.SetName(button, label);
    }
}
