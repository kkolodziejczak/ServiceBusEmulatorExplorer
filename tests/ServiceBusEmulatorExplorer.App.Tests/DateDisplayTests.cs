using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using ServiceBusEmulatorExplorer.App.Investigation;
using ServiceBusEmulatorExplorer.App.Investigation.Settings;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.App.Tests;

public sealed class DateDisplayTests
{
    [Fact]
    public void Windows_format_uses_the_supplied_culture_short_date_pattern_without_changing_thread_culture()
    {
        DateTime value = new(2026, 10, 4);
        CultureInfo culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd.MM.yyyy";
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        CultureInfo currentUiCulture = CultureInfo.CurrentUICulture;

        string formatted = DateDisplay.Date(value, DateDisplayFormat.Windows, culture);

        Assert.Equal("04.10.2026", formatted);
        Assert.Equal(new DateTime(2026, 10, 4), value);
        Assert.Same(currentCulture, CultureInfo.CurrentCulture);
        Assert.Same(currentUiCulture, CultureInfo.CurrentUICulture);
    }

    [Theory]
    [InlineData(DateDisplayFormat.Iso, "2026-10-04")]
    [InlineData(DateDisplayFormat.DayFirst, "04/10/2026")]
    [InlineData(DateDisplayFormat.MonthFirst, "10/04/2026")]
    public void Numeric_formats_are_unambiguous_for_the_fourth_of_october(DateDisplayFormat format, string expected)
    {
        Assert.Equal(expected, DateDisplay.Date(new DateTime(2026, 10, 4), format, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Named_month_format_uses_the_supplied_culture()
    {
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");

        string formatted = DateDisplay.Date(new DateTime(2026, 10, 4), DateDisplayFormat.NamedMonth, french);

        Assert.Equal(new DateTime(2026, 10, 4).ToString("dd MMM yyyy", french), formatted);
        Assert.Contains("oct", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Timestamp_keeps_the_offset_clock_time_and_does_not_mutate_the_instant_or_thread_culture()
    {
        DateTimeOffset instant = new(2026, 10, 4, 14, 30, 45, TimeSpan.FromHours(4));
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        CultureInfo currentUiCulture = CultureInfo.CurrentUICulture;

        string formatted = DateDisplay.Timestamp(instant, DateDisplayFormat.Iso);

        Assert.Equal("2026-10-04 14:30:45", formatted);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 14, 30, 45, TimeSpan.FromHours(4)), instant);
        Assert.Same(currentCulture, CultureInfo.CurrentCulture);
        Assert.Same(currentUiCulture, CultureInfo.CurrentUICulture);
    }

    [Theory]
    [InlineData(DateDisplayFormat.Windows)]
    [InlineData(DateDisplayFormat.Iso)]
    [InlineData(DateDisplayFormat.DayFirst)]
    [InlineData(DateDisplayFormat.MonthFirst)]
    [InlineData(DateDisplayFormat.NamedMonth)]
    public async Task Protected_preferences_round_trip_each_date_format(DateDisplayFormat format)
    {
        using var fixture = new PreferencesFile();
        var store = new ProtectedWorkspacePreferencesStore(fixture.Path);

        await store.SaveAsync(new WorkspacePreferences { DateFormat = format }, CancellationToken.None);
        PreferencesLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(format, loaded.Preferences.DateFormat);
    }

    [Fact]
    public async Task Older_protected_preferences_without_date_format_use_windows_default()
    {
        using var fixture = new PreferencesFile();
        var store = new ProtectedWorkspacePreferencesStore(fixture.Path);
        await store.SaveAsync(new WorkspacePreferences { DateFormat = DateDisplayFormat.Iso }, CancellationToken.None);
        JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
        envelope["settings"]!.AsObject().Remove("dateFormat");
        await File.WriteAllTextAsync(fixture.Path, envelope.ToJsonString());

        PreferencesLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(DateDisplayFormat.Windows, loaded.Preferences.DateFormat);
    }

    [Fact]
    public async Task Unknown_persisted_date_format_falls_back_to_windows_default()
    {
        using var fixture = new PreferencesFile();
        var store = new ProtectedWorkspacePreferencesStore(fixture.Path);
        await store.SaveAsync(new WorkspacePreferences { DateFormat = DateDisplayFormat.Iso }, CancellationToken.None);
        JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
        envelope["settings"]!["dateFormat"] = 99;
        await File.WriteAllTextAsync(fixture.Path, envelope.ToJsonString());

        PreferencesLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.Warning);
        Assert.Equal(DateDisplayFormat.Windows, loaded.Preferences.DateFormat);
    }

    private sealed class PreferencesFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "sbe-date-display-preferences-" + Guid.NewGuid().ToString("N"), "preferences.json");

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
            string directory = System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }
}
