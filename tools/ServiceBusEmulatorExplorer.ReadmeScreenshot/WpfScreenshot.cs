using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class WpfScreenshot
{
    private const int MinimumWidth = 1200;
    private const int MinimumHeight = 650;
    internal const int CaptureWidth = 1484;
    internal const int CaptureHeight = 961;

    public static void SaveWindowContent(Window window, string outputPath) =>
        SaveWindowContent(window, outputPath, CaptureWidth, CaptureHeight);

    public static void SaveWindowContent(Window window, string outputPath, int expectedWidth, int expectedHeight, int minimumBytes = 10_000)
        => SaveWindowContent(window, outputPath, expectedWidth, expectedHeight, minimumBytes, popup: null);

    public static void SaveWindowContentWithPopup(Window window, Popup popup, string outputPath,
        int expectedWidth, int expectedHeight, int minimumBytes = 10_000)
        => SaveWindowContent(window, outputPath, expectedWidth, expectedHeight, minimumBytes, popup);

    private static void SaveWindowContent(Window window, string outputPath, int expectedWidth, int expectedHeight,
        int minimumBytes, Popup? popup)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("The main window does not contain a renderable WPF element.");
        }

        content.UpdateLayout();

        int width = (int)Math.Ceiling(content.ActualWidth);
        int height = (int)Math.Ceiling(content.ActualHeight);
        if (width != expectedWidth || height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"The rendered app surface has unstable dimensions: {width}x{height}. Expected {expectedWidth}x{expectedHeight}.");
        }

        if (expectedWidth == CaptureWidth && expectedHeight == CaptureHeight && (width < MinimumWidth || height < MinimumHeight))
            throw new InvalidOperationException("The rendered app surface is smaller than the supported README viewport.");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var captureVisual = new DrawingVisual();
        Vector contentOffset = VisualTreeHelper.GetOffset(content);
        using (DrawingContext drawingContext = captureVisual.RenderOpen())
        {
            var contentBrush = new VisualBrush(content)
            {
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(contentOffset.X, contentOffset.Y, width, height),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, width, height)
            };
            drawingContext.DrawRectangle(contentBrush, null, new Rect(0, 0, width, height));
            if (popup?.IsOpen == true && popup.Child is FrameworkElement popupContent)
            {
                popupContent.UpdateLayout();
                double popupWidth = popupContent.ActualWidth;
                double popupHeight = popupContent.ActualHeight;
                if (popupWidth > 0 && popupHeight > 0)
                {
                    var popupBitmap = new RenderTargetBitmap(
                        (int)Math.Ceiling(popupWidth), (int)Math.Ceiling(popupHeight), 96, 96, PixelFormats.Pbgra32);
                    popupBitmap.Render(popupContent);
                    Point popupOrigin = content.PointFromScreen(popupContent.PointToScreen(new Point(0, 0)));
                    drawingContext.DrawImage(popupBitmap, new Rect(popupOrigin.X, popupOrigin.Y, popupWidth, popupHeight));
                }
            }
        }

        bitmap.Render(captureVisual);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(outputPath);
        encoder.Save(output);

        long fileSize = new FileInfo(outputPath).Length;
        if (fileSize < minimumBytes)
            throw new InvalidOperationException($"The rendered README screenshot is unexpectedly small: {fileSize} bytes.");

        Console.WriteLine($"Captured {width}x{height} README screenshot ({fileSize} bytes) at {outputPath}");
    }
}
