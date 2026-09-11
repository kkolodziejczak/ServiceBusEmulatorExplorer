using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServiceBusEmulatorExplorer.InvestigationPrototype;

internal static class ProofCapture
{
    public static void Save(Window window, string directory, string name)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
            (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    public static T Control<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        return Descendants(root).OfType<T>().FirstOrDefault(element => element.Name == name
            || AutomationProperties.GetAutomationId(element) == name)
            ?? throw new InvalidOperationException($"Visible control missing: {name}");
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    public static void CheckBounds(Window window, IEnumerable<FrameworkElement> controls)
    {
        var content = (FrameworkElement)window.Content;
        var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        foreach (var control in controls.Where(control => control.IsVisible))
        {
            var rectangle = control.TransformToAncestor(content)
                .TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
            if (rectangle.Width < 1 || rectangle.Height < 1 || !bounds.Contains(rectangle))
                throw new InvalidOperationException($"Control outside window: {control.Name} {rectangle}");
        }
    }
}
