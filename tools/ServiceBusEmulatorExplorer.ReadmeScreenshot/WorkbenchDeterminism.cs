using System.Globalization;
using ServiceBusEmulatorExplorer.App.Investigation;

namespace ServiceBusEmulatorExplorer.ReadmeScreenshot;

/// <summary>
/// Fixes the Message Workbench's clock, generated event IDs and culture so the same scenario renders the same pixels
/// on every run and machine, which the Workbench visual baselines rely on (DEC-057).
/// </summary>
internal static class WorkbenchDeterminism
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The fixed clock: 2026-10-18 09:30 UTC, with UTC as the local time zone on every machine.</summary>
    public static TimeProvider Clock { get; } = new FixedClock();

    /// <summary>Today on <see cref="Clock"/>.</summary>
    public static DateTime Today => FixedUtc.UtcDateTime.Date;

    /// <summary>
    /// Formats dates and numbers as plain en-US, without this machine's regional overrides, so the date picker and other
    /// culture-formatted text render the same on every machine.
    /// </summary>
    public static void UseFixedCulture()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static void Apply(MessageLibraryPrototypeDialog dialog) =>
        ((MessageLibraryPrototypeReviewSurface)dialog.FindName("ReviewSurfaceControl")!).UseClock(Clock);

    public static void Apply(MessageLibraryPrototypeView workbench)
    {
        workbench.Clock = Clock;
        int next = 0;
        workbench.NewEventId = () => new Guid($"00000000-0000-4000-8000-{++next:D12}");
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
