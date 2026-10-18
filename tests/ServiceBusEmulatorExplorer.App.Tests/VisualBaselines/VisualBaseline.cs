using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ServiceBusEmulatorExplorer.ReadmeScreenshot;

namespace ServiceBusEmulatorExplorer.App.Tests.VisualBaselines;

/// <summary>
/// Compares a rendered window, pixel for pixel, with its committed baseline (DEC-049).
/// Set SBE_UPDATE_VISUAL_BASELINES=true to rewrite baselines after an approved visual change.
/// </summary>
internal static class VisualBaseline
{
    private const string UpdateVariable = "SBE_UPDATE_VISUAL_BASELINES";
    private const string EnvironmentFile = "environment.json";
    private static readonly HashSet<string> CheckedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Windows are rendered without their frame; other elements, such as popup content, are rendered as they are.</summary>
    public static void AssertMatches(FrameworkElement element, string name, Popup? popup = null)
    {
        lock (CheckedNames)
            Assert.True(CheckedNames.Add(name), $"Visual baseline '{name}' was checked twice in one run; give each captured state its own name.");
        byte[] actualPng = EncodePng(Render(element, popup));
        string baselinePath = Path.Combine(BaselineDirectory(), name + ".png");
        if (UpdateRequested())
        {
            File.WriteAllBytes(baselinePath, actualPng);
            File.WriteAllText(Path.Combine(BaselineDirectory(), EnvironmentFile), DescribeEnvironment());
            return;
        }

        Assert.True(File.Exists(baselinePath),
            $"Visual baseline '{name}' is missing at {baselinePath}. Render it with {UpdateVariable}=true once the screen is approved.");
        Pixels expected = Decode(File.ReadAllBytes(baselinePath));
        Pixels actual = Decode(actualPng);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            string actualPath = WriteArtifact(name + ".actual.png", actualPng);
            Assert.Fail($"Visual baseline '{name}' is {expected.Width}x{expected.Height} but the screen rendered " +
                $"{actual.Width}x{actual.Height}. Actual: {actualPath}. {EnvironmentNote()}");
        }

        Int32Rect changed = FindChangedArea(expected, actual, out int changedPixels, out Pixels diff);
        if (changedPixels == 0) return;

        string actualArtifact = WriteArtifact(name + ".actual.png", actualPng);
        WriteArtifact(name + ".diff.png", EncodePng(diff.ToBitmap()));
        Assert.Fail($"Visual baseline '{name}' differs in {changedPixels} pixels within " +
            $"x={changed.X}, y={changed.Y}, {changed.Width}x{changed.Height}. Actual and diff: {Path.GetDirectoryName(actualArtifact)}. " +
            EnvironmentNote());
    }

    /// <summary>
    /// Renders without hover styling: the test window sits on the real desktop, so wherever the physical cursor
    /// happens to rest would otherwise change the pixels.
    /// </summary>
    private static BitmapSource Render(FrameworkElement element, Popup? popup)
    {
        UIElement[] surfaces = new UIElement?[] { element is Window { Content: UIElement content } ? content : element, popup?.Child }
            .OfType<UIElement>().ToArray();
        bool[] hitTestVisible = surfaces.Select(surface => surface.IsHitTestVisible).ToArray();
        foreach (UIElement surface in surfaces) surface.IsHitTestVisible = false;
        Mouse.Synchronize();
        try
        {
            if (element is Window window) return WpfScreenshot.RenderWindowContent(window, popup);
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)),
                Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            for (int index = 0; index < surfaces.Length; index++) surfaces[index].IsHitTestVisible = hitTestVisible[index];
            Mouse.Synchronize();
        }
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string BaselineDirectory([CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;

    private static bool UpdateRequested() =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "true", StringComparison.OrdinalIgnoreCase);

    private static string DescribeEnvironment() => JsonSerializer.Serialize(new
    {
        windows = Environment.OSVersion.VersionString,
        culture = CultureInfo.CurrentCulture.Name,
        dpi = 96,
        segoeUi = FontFingerprint("segoeui.ttf"),
        consolas = FontFingerprint("consola.ttf")
    }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

    private static string FontFingerprint(string fileName)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fileName);
        if (!File.Exists(path)) return "missing";
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..16];
    }

    /// <summary>Decodes through the same PNG round trip as the baseline, so only real rendering differences remain.</summary>
    private static Pixels Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        BitmapSource frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var data = new int[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(data, converted.PixelWidth * 4, 0);
        return new Pixels(converted.PixelWidth, converted.PixelHeight, data);
    }

    private static string WriteArtifact(string fileName, byte[] png)
    {
        string directory = Path.Combine(BaselineDirectory(), "..", "..", "..", "artifacts", "visual-baselines");
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, fileName));
        File.WriteAllBytes(path, png);
        return path;
    }

    private static string EnvironmentNote()
    {
        string recordedPath = Path.Combine(BaselineDirectory(), EnvironmentFile);
        string current = DescribeEnvironment();
        if (!File.Exists(recordedPath)) return $"No recorded baseline environment. Current environment: {current}";
        string recorded = File.ReadAllText(recordedPath);
        return string.Equals(recorded.ReplaceLineEndings(), current.ReplaceLineEndings(), StringComparison.Ordinal)
            ? "The rendering environment matches the one the baselines were recorded in, so the UI itself changed."
            : $"The rendering environment differs from the recorded one. Recorded: {recorded} Current: {current}";
    }

    private static Int32Rect FindChangedArea(Pixels expected, Pixels actual, out int changedPixels, out Pixels diff)
    {
        const int Highlight = unchecked((int)0xFFFF00FF);
        var diffData = new int[expected.Data.Length];
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        changedPixels = 0;
        for (int index = 0; index < expected.Data.Length; index++)
        {
            if (expected.Data[index] == actual.Data[index])
            {
                diffData[index] = Dim(expected.Data[index]);
                continue;
            }

            diffData[index] = Highlight;
            changedPixels++;
            int x = index % expected.Width, y = index / expected.Width;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }

        diff = expected with { Data = diffData };
        return changedPixels == 0 ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    private static int Dim(int bgra)
    {
        int gray = (((bgra >> 16) & 0xFF) + ((bgra >> 8) & 0xFF) + (bgra & 0xFF)) / 3;
        int light = 192 + gray / 4;
        return unchecked((int)0xFF000000) | (light << 16) | (light << 8) | light;
    }

    private sealed record Pixels(int Width, int Height, int[] Data)
    {
        public BitmapSource ToBitmap() =>
            BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Data, Width * 4);
    }
}
