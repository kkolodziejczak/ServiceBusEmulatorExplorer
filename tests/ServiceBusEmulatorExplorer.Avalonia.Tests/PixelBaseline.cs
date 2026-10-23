using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ServiceBusEmulatorExplorer.Avalonia.Tests;

/// <summary>
/// Pixel comparison for Avalonia renders (DEC-050, DEC-058): one baseline for every operating system. Outside text
/// every pixel must match exactly. Each OS rasterizes glyphs with its own font engine, so inside the bounds of text
/// elements the images are compared after a 3x3 box blur, which absorbs anti-aliasing but not a moved, resized or
/// changed glyph. Renders and baselines are decoded through the same PNG path. Every render, and its comparison with
/// the WPF baseline of the same screen, is written to <see cref="RenderDirectory"/> for side-by-side review.
/// </summary>
internal static class PixelBaseline
{
    private const string UpdateVariable = "SBE_UPDATE_VISUAL_BASELINES";

    public static string RenderDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "avalonia-renders");

    /// <summary>
    /// The largest blurred channel difference allowed inside text (DEC-058). Calibrated on 2026-10-23: Ubuntu and macOS
    /// renders differ from the Windows baseline by up to 25 and 29; the heading moved by 1 px differs by 76.
    /// </summary>
    public const int TextTolerance = 45;

    public static void AssertMatches(Bitmap render, Window window, string name)
    {
        Directory.CreateDirectory(RenderDirectory);
        string renderPath = Path.Combine(RenderDirectory, name + ".png");
        render.Save(renderPath, PngBitmapEncoderOptions.Default);
        string baselinePath = BaselinePath(name);
        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "true", StringComparison.OrdinalIgnoreCase))
            File.Copy(renderPath, baselinePath, overwrite: true);

        Assert.True(File.Exists(baselinePath), $"No Avalonia baseline {name}; review the render at {renderPath} first.");
        var (actual, actualSize) = Decode(renderPath);
        var (expected, expectedSize) = Decode(baselinePath);
        Assert.Equal(expectedSize, actualSize);
        var (outsideText, textDelta) = Compare(expected, actual, TextMask(window, actualSize), actualSize);
        Assert.True(outsideText == 0 && textDelta <= TextTolerance,
            $"{name}: {outsideText} pixels outside text differ from the baseline, and text differs by up to {textDelta} " +
            $"after blurring (allowed {TextTolerance}); render at {renderPath}.");
    }

    /// <summary>Marks the pixels covered by text elements, widened by one pixel for glyphs that overhang their box.</summary>
    public static bool[] TextMask(Window window, PixelSize size)
    {
        var mask = new bool[size.Width * size.Height];
        foreach (var text in window.GetVisualDescendants().OfType<Control>().Where(control =>
                     control is TextBlock or TextPresenter && control.IsEffectivelyVisible))
        {
            if (text.TranslatePoint(default, window) is not { } origin) continue;
            var bounds = new Rect(origin, text.Bounds.Size).Inflate(1);
            for (int y = Math.Max(0, (int)Math.Floor(bounds.Top)); y < Math.Min(size.Height, (int)Math.Ceiling(bounds.Bottom)); y++)
                for (int x = Math.Max(0, (int)Math.Floor(bounds.Left)); x < Math.Min(size.Width, (int)Math.Ceiling(bounds.Right)); x++)
                    mask[y * size.Width + x] = true;
        }
        return mask;
    }

    /// <summary>Counts differing pixels outside text, and the largest blurred channel difference inside it.</summary>
    public static (int OutsideText, int TextDelta) Compare(byte[] expected, byte[] actual, bool[] textMask, PixelSize size)
    {
        int outsideText = 0, textDelta = 0;
        for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int pixel = (y * size.Width + x) * 4;
                if (!textMask[y * size.Width + x])
                {
                    if (!expected.AsSpan(pixel, 4).SequenceEqual(actual.AsSpan(pixel, 4))) outsideText++;
                    continue;
                }
                for (int channel = 0; channel < 3; channel++)
                    textDelta = Math.Max(textDelta, Math.Abs(Blurred(expected, size, x, y, channel) - Blurred(actual, size, x, y, channel)));
            }
        return (outsideText, textDelta);
    }

    private static int Blurred(byte[] pixels, PixelSize size, int x, int y, int channel)
    {
        int sum = 0, count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= size.Width || ny >= size.Height) continue;
                sum += pixels[(ny * size.Width + nx) * 4 + channel];
                count++;
            }
        return sum / count;
    }

    /// <summary>Writes how the render differs from the WPF baseline: a report line and a diff image (red = differing pixel).</summary>
    public static string CompareWithWpf(Bitmap render, string wpfBaseline, string name)
    {
        Directory.CreateDirectory(RenderDirectory);
        string renderPath = Path.Combine(RenderDirectory, name + ".png");
        render.Save(renderPath, PngBitmapEncoderOptions.Default);
        string wpfPath = Path.Combine(RepositoryRoot, "tests", "ServiceBusEmulatorExplorer.App.Tests", "VisualBaselines", wpfBaseline + ".png");
        File.Copy(wpfPath, Path.Combine(RenderDirectory, name + "-wpf.png"), overwrite: true);
        var (avalonia, avaloniaSize) = Decode(renderPath);
        var (wpf, wpfSize) = Decode(wpfPath);
        string report;
        if (avaloniaSize != wpfSize)
            report = $"{name}: size {avaloniaSize.Width}x{avaloniaSize.Height}, WPF {wpfSize.Width}x{wpfSize.Height}";
        else
        {
            int differing = CountDifferences(wpf, avalonia, out int largestDelta);
            WriteDiff(wpf, avalonia, wpfSize, Path.Combine(RenderDirectory, name + "-diff.png"));
            report = $"{name}: {differing} of {wpfSize.Width * wpfSize.Height} pixels differ from WPF " +
                $"({100.0 * differing / (wpfSize.Width * wpfSize.Height):0.0}%), largest channel delta {largestDelta}";
        }
        File.AppendAllText(Path.Combine(RenderDirectory, "wpf-comparison.txt"), report + Environment.NewLine);
        return report;
    }

    public static string BaselinePath(string name) =>
        Path.Combine(RepositoryRoot, "tests", "ServiceBusEmulatorExplorer.Avalonia.Tests", "Baselines", name + ".png");

    public static (byte[] Pixels, PixelSize Size) Decode(string path)
    {
        using var bitmap = new Bitmap(path);
        var size = bitmap.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4); }
        finally { handle.Free(); }
        return (pixels, size);
    }

    private static int CountDifferences(byte[] expected, byte[] actual, out int largestDelta)
    {
        int differing = 0;
        largestDelta = 0;
        for (int pixel = 0; pixel < expected.Length; pixel += 4)
        {
            int delta = 0;
            for (int channel = 0; channel < 4; channel++)
                delta = Math.Max(delta, Math.Abs(expected[pixel + channel] - actual[pixel + channel]));
            if (delta == 0) continue;
            differing++;
            largestDelta = Math.Max(largestDelta, delta);
        }
        return differing;
    }

    // Differing pixels are red; equal ones are a faded grey copy of the image, so the layout stays recognisable.
    private static void WriteDiff(byte[] expected, byte[] actual, PixelSize size, string path)
    {
        using var diff = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = diff.Lock())
        {
            var pixels = new byte[expected.Length];
            for (int pixel = 0; pixel < expected.Length; pixel += 4)
            {
                bool same = expected.AsSpan(pixel, 4).SequenceEqual(actual.AsSpan(pixel, 4));
                byte grey = (byte)(215 + (expected[pixel] + expected[pixel + 1] + expected[pixel + 2]) / 3 * 40 / 255);
                (pixels[pixel], pixels[pixel + 1], pixels[pixel + 2]) = same ? (grey, grey, grey) : ((byte)0, (byte)0, (byte)220);
                pixels[pixel + 3] = 255;
            }
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        diff.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", ".."));

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}
