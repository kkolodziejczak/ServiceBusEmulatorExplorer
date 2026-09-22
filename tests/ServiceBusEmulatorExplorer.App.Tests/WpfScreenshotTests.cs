using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests;

[Collection("WPF presentation")]
public sealed class WpfScreenshotTests
{
    private const int Width = 96;
    private const int Height = 64;

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void SaveWindowContent_captures_margin_content_at_local_origin()
    {
        RunOnSta(() => AssertCapture(new Thickness(11, 7, 19, 13)));
    }

    [Fact]
    [Trait("TestCategory", "UiRender")]
    public void SaveWindowContent_captures_content_without_margin()
    {
        RunOnSta(() => AssertCapture(new Thickness(0)));
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The WPF screenshot proof exceeded its 30-second bound.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void AssertCapture(Thickness margin)
    {
        var content = new Border
        {
            Width = Width,
            Height = Height,
            Margin = margin,
            Child = CreateEdgeFixture()
        };
        var window = new Window
        {
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -1000,
            Top = -1000
        };
        string outputPath = Path.Combine(Path.GetTempPath(), $"WpfScreenshotTests-{Guid.NewGuid():N}.png");

        try
        {
            window.Show();
            window.UpdateLayout();
            content.UpdateLayout();

            WpfScreenshot.SaveWindowContent(window, outputPath, Width, Height);

            using var input = File.OpenRead(outputPath);
            var frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(Width, frame.PixelWidth);
            Assert.Equal(Height, frame.PixelHeight);

            byte[] pixels = new byte[Width * Height * 4];
            frame.CopyPixels(pixels, Width * 4, 0);
            AssertPixel(pixels, Width / 2, 0, 220, 40, 40);
            AssertPixel(pixels, Width - 1, Height / 2, 40, 180, 40);
            AssertPixel(pixels, Width / 2, Height - 1, 40, 70, 210);
            AssertPixel(pixels, 0, Height / 2, 220, 180, 40);
            Assert.All(pixels.Chunk(4), pixel => Assert.Equal(255, pixel[3]));
        }
        finally
        {
            window.Close();
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static Grid CreateEdgeFixture()
    {
        var surface = new Grid
        {
            Width = Width,
            Height = Height,
            Background = Brushes.Transparent
        };

        var interior = new Image
        {
            Source = CreateTexture(Width - 16, Height - 16),
            Stretch = Stretch.Fill,
            Margin = new Thickness(8)
        };
        surface.Children.Add(interior);
        surface.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(220, 40, 40)), Height = 8, VerticalAlignment = VerticalAlignment.Top });
        surface.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(40, 180, 40)), Width = 8, HorizontalAlignment = HorizontalAlignment.Right });
        surface.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(40, 70, 210)), Height = 8, VerticalAlignment = VerticalAlignment.Bottom });
        surface.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(220, 180, 40)), Width = 8, HorizontalAlignment = HorizontalAlignment.Left });
        return surface;
    }

    private static WriteableBitmap CreateTexture(int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        byte[] pixels = new byte[width * height * 4];
        new Random(1731).NextBytes(pixels);
        for (int offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AssertPixel(byte[] pixels, int x, int y, byte red, byte green, byte blue)
    {
        int offset = (y * Width + x) * 4;
        Assert.Equal(blue, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(red, pixels[offset + 2]);
        Assert.Equal(255, pixels[offset + 3]);
    }
}
