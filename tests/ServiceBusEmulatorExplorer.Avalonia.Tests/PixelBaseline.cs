using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ServiceBusEmulatorExplorer.Avalonia.Tests;

/// <summary>
/// Exact pixel comparison for Avalonia renders (DEC-050, DEC-058). Renders and baselines are decoded through the same
/// PNG path, so equal images compare equal byte for byte. Every render, and its comparison with the WPF baseline of the
/// same screen, is written to <see cref="RenderDirectory"/> for side-by-side review.
/// </summary>
internal static class PixelBaseline
{
    private const string UpdateVariable = "SBE_UPDATE_VISUAL_BASELINES";

    public static string RenderDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "avalonia-renders");

    public static void AssertMatches(Bitmap render, string name)
    {
        Directory.CreateDirectory(RenderDirectory);
        string renderPath = Path.Combine(RenderDirectory, name + ".png");
        render.Save(renderPath, PngBitmapEncoderOptions.Default);
        string baselinePath = Path.Combine(RepositoryRoot, "tests", "ServiceBusEmulatorExplorer.Avalonia.Tests", "Baselines", name + ".png");
        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "true", StringComparison.OrdinalIgnoreCase))
            File.Copy(renderPath, baselinePath, overwrite: true);

        Assert.True(File.Exists(baselinePath), $"No Avalonia baseline {name}; review the render at {renderPath} first.");
        var (actual, actualSize) = Decode(renderPath);
        var (expected, expectedSize) = Decode(baselinePath);
        Assert.Equal(expectedSize, actualSize);
        int differing = CountDifferences(expected, actual, out int largestDelta);
        Assert.True(differing == 0, $"{name}: {differing} pixels differ from the baseline (largest channel delta {largestDelta}); render at {renderPath}.");
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

    private static (byte[] Pixels, PixelSize Size) Decode(string path)
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
