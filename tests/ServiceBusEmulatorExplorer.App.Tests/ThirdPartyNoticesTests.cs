using System.Collections;
using System.IO;
using System.Resources;
using System.Text.Json;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;

namespace ServiceBusEmulatorExplorer.App.Tests;

/// <summary>
/// Keeps Settings > Licences complete without anyone remembering to (DEC-052): every package the app ships and every
/// bundled font must have exactly one notice, and no notice may name something that no longer ships.
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    private const string FontSuffix = ".ttf";

    [Fact]
    public void Every_shipped_package_has_one_notice_and_no_notice_is_stale()
    {
        string[] noticed = ThirdPartyNotices.Components.SelectMany(component => component.Includes)
            .Where(item => !item.EndsWith(FontSuffix, StringComparison.OrdinalIgnoreCase)).ToArray();

        AssertSameItems(ShippedPackages(), noticed, "package");
    }

    [Fact]
    public void Every_bundled_font_file_has_one_notice_and_no_notice_is_stale()
    {
        string[] noticed = ThirdPartyNotices.Components.SelectMany(component => component.Includes)
            .Where(item => item.EndsWith(FontSuffix, StringComparison.OrdinalIgnoreCase)).ToArray();

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

    /// <summary>Font files compiled into the app's resources, so a font added through the project glob counts too.</summary>
    private static string[] BundledFontFiles()
    {
        var app = typeof(ThirdPartyNotices).Assembly;
        using Stream resources = app.GetManifestResourceStream("ServiceBusEmulatorExplorer.App.g.resources")!;
        using var reader = new ResourceReader(resources);
        return reader.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => key.StartsWith("assets/fonts/", StringComparison.Ordinal) && key.EndsWith(FontSuffix, StringComparison.Ordinal))
            .Select(Path.GetFileName).OfType<string>()
            .ToArray();
    }

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
