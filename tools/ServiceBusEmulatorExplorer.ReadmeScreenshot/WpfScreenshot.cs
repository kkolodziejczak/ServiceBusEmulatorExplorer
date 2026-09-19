using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

internal static class WpfScreenshot
{
    private const int MinimumWidth = 1200;
    private const int MinimumHeight = 650;
    private const int CaptureWidth = 1484;
    private const int CaptureHeight = 961;

    public static void SaveWindowContent(Window window, string outputPath)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("The main window does not contain a renderable WPF element.");
        }

        content.Measure(new Size(CaptureWidth, CaptureHeight));
        content.Arrange(new Rect(0, 0, CaptureWidth, CaptureHeight));
        content.UpdateLayout();

        int width = (int)Math.Ceiling(content.ActualWidth);
        int height = (int)Math.Ceiling(content.ActualHeight);
        if (width != CaptureWidth || height != CaptureHeight)
        {
            throw new InvalidOperationException(
                $"The rendered app surface has unstable dimensions: {width}x{height}. Expected {CaptureWidth}x{CaptureHeight}.");
        }

        if (width < MinimumWidth || height < MinimumHeight)
            throw new InvalidOperationException("The rendered app surface is smaller than the supported README viewport.");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

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
        if (fileSize < 10_000)
            throw new InvalidOperationException($"The rendered README screenshot is unexpectedly small: {fileSize} bytes.");

        Console.WriteLine($"Captured {width}x{height} README screenshot ({fileSize} bytes) at {outputPath}");
    }
}
