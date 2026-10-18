using System.Collections;
using System.IO;
using System.Resources;
using System.Text.Json;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Keeps Settings > Licences complete without anyone remembering to (DEC-052): every package and shared framework the
/// app ships and every bundled font must have exactly one notice, and no notice may name something that no longer ships.
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    private static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc"];

    [Fact]
    public void Every_shipped_package_and_framework_has_one_notice_and_no_notice_is_stale()
    {
        string[] noticed = ThirdPartyNotices.Components.SelectMany(component => component.Includes).Where(item => !IsFont(item)).ToArray();

        AssertSameItems([.. ShippedPackages(), .. ShippedFrameworks()], noticed, "package or framework");
    }

    [Fact]
    public void Every_bundled_font_file_has_one_notice_and_no_notice_is_stale()
    {
        string[] noticed = ThirdPartyNotices.Components.SelectMany(component => component.Includes).Where(IsFont).ToArray();

        AssertSameItems(BundledFontFiles(), noticed, "font file");
    }

    [Fact]
    public void Every_notice_carries_its_copyright_and_licence_text()
    {
        foreach (ThirdPartyComponent component in ThirdPartyNotices.Components)
        {
            string notice = component.Notice;
            Assert.Contains("Copyright", notice, StringComparison.OrdinalIgnoreCase);
            Assert.True(notice.Contains("Permission is hereby granted", StringComparison.Ordinal)
                || notice.Contains("SIL Open Font License", StringComparison.Ordinal),
                $"{component.Name}: the bundled text is neither the MIT nor the SIL OFL licence.");
        }
    }

    [Fact]
    public void The_bundled_runtime_carries_its_third_party_notices()
    {
        string notice = ThirdPartyNotices.Components.Single(component => component.Includes.Contains("Microsoft.NETCore.App")).Notice;

        Assert.Contains(".NET runtime third-party notices", notice, StringComparison.Ordinal);
        Assert.Contains("WPF third-party notices", notice, StringComparison.Ordinal);
        Assert.Contains("Windows Forms third-party notices", notice, StringComparison.Ordinal);
    }

    /// <summary>The app's own package closure, as the build resolved it for the shipped executable.</summary>
    private static string[] ShippedPackages()
    {
        string depsPath = Path.Combine(AppContext.BaseDirectory, "ServiceBusEmulatorExplorer.App.deps.json");
        using JsonDocument deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        return deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "package")
            .Select(library => library.Name.Split('/')[0])
            .ToArray();
    }

    /// <summary>Shared frameworks the app runs on; the portable release bundles them.</summary>
    private static string[] ShippedFrameworks()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "ServiceBusEmulatorExplorer.App.runtimeconfig.json");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
        return config.RootElement.GetProperty("runtimeOptions").GetProperty("frameworks").EnumerateArray()
            .Select(framework => framework.GetProperty("name").GetString()!)
            .ToArray();
    }

    /// <summary>Font files compiled anywhere into the app's resources, so a font added through the project counts too.</summary>
    private static string[] BundledFontFiles()
    {
        var app = typeof(ThirdPartyNotices).Assembly;
        using Stream resources = app.GetManifestResourceStream("ServiceBusEmulatorExplorer.App.g.resources")!;
        using var reader = new ResourceReader(resources);
        return reader.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(IsFont)
            .Select(Path.GetFileName).OfType<string>()
            .ToArray();
    }

    private static bool IsFont(string name) =>
        FontExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    private static void AssertSameItems(string[] shipped, string[] noticed, string kind)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        string[] duplicated = noticed.GroupBy(item => item, comparer).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        string[] missing = shipped.Except(noticed, comparer).Order().ToArray();
        string[] stale = noticed.Except(shipped, comparer).Order().ToArray();
        Assert.True(missing.Length == 0 && stale.Length == 0 && duplicated.Length == 0,
            $"ThirdPartyNotices is out of date. Add a {kind} notice for: [{string.Join(", ", missing)}]; " +
            $"remove: [{string.Join(", ", stale)}]; listed twice: [{string.Join(", ", duplicated)}].");
    }
}
