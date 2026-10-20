using System.Globalization;
using ServiceBusEmulatorExplorer.Core.Investigation;

namespace ServiceBusEmulatorExplorer.Presentation.Workbench;

/// <summary>The time a review is asked to schedule its messages for: a day, an hour and a minute, in UTC or local time.</summary>
public sealed record ScheduleChoice(DateTime? Date, string? Hour, string? Minute, bool Local);

/// <summary>The schedule field the user changes to fix a problem.</summary>
public enum ScheduleField { Date, Time }

/// <summary>Why a schedule cannot be used, and the field that fixes it.</summary>
public sealed record ScheduleProblem(string Message, ScheduleField Field);

/// <summary>A schedule resolved to its UTC instant, or the problem that stops it.</summary>
public sealed record ScheduleResolution(DateTime Utc, ScheduleProblem? Problem);

/// <summary>
/// What the review says about when its messages go: the confirm button, the notice, whether the user may confirm, the
/// schedule problem, and the resolved time (<c>null</c> when sending now leaves it as it was).
/// </summary>
public sealed record ReviewTiming(string ConfirmText, string Notice, bool CanConfirm, ScheduleProblem? Problem, string? ResolvedText);

/// <summary>
/// The Message Workbench review's timing: sending now or at one scheduled instant, and what is wrong with a schedule
/// that has no day, no time, a time the local clocks skip or repeat, or a time already past.
/// </summary>
public static class WorkbenchSchedule
{
    public static ReviewTiming SendNow(int count, bool queue) => new(
        ConfirmText("Send", count),
        queue ? count == 1 ? "This message will be sent to the queue." : $"{count} messages will be sent to the queue."
            : "Subscriptions receive messages according to their rules.",
        CanConfirm: true, Problem: null, ResolvedText: null);

    /// <param name="zone">The time zone a local schedule is read in.</param>
    public static ReviewTiming Schedule(int count, ScheduleChoice choice, DateTime nowUtc, TimeZoneInfo zone, DateDisplayFormat format)
    {
        var resolution = Resolve(choice, nowUtc, zone, format);
        return new(ConfirmText("Schedule", count),
            $"{count} sample message{Plural(count)} will be scheduled for the same instant.",
            resolution.Problem is null, resolution.Problem,
            resolution.Problem is null
                ? $"Resolved time: {DateDisplay.Timestamp(new DateTimeOffset(resolution.Utc), format, seconds: false)} UTC · one time for all messages"
                : "One time for all messages");
    }

    private static string ConfirmText(string verb, int count) => $"{verb} {count} message{Plural(count)}";

    private static string Plural(int count) => count == 1 ? "" : "s";

    /// <summary>
    /// Resolves the chosen day and time to a UTC instant later than <paramref name="nowUtc"/>, or names what is wrong
    /// with it and the field to change.
    /// </summary>
    public static ScheduleResolution Resolve(ScheduleChoice choice, DateTime nowUtc, TimeZoneInfo zone, DateDisplayFormat format)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(zone);
        if (choice.Date is not DateTime date)
            return Problem("Choose a date for the scheduled send.", ScheduleField.Date);
        if (!int.TryParse(choice.Hour, NumberStyles.None, CultureInfo.InvariantCulture, out int hour) ||
            !int.TryParse(choice.Minute, NumberStyles.None, CultureInfo.InvariantCulture, out int minute) ||
            hour is < 0 or > 23 || minute is < 0 or > 59)
            return Problem("Choose an hour and a minute for the scheduled send.", ScheduleField.Time);

        string zoneName = choice.Local ? "local time" : "UTC";
        DateTime wallTime = DateTime.SpecifyKind(date.Date.AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
        string selected = $"{DateDisplay.Date(date, format)} {wallTime.ToString("HH:mm", CultureInfo.InvariantCulture)} {zoneName}";
        DateTime utc;
        if (choice.Local)
        {
            if (zone.IsInvalidTime(wallTime))
                return Problem($"{selected} does not exist because the clocks move forward. Choose another time or use UTC.", ScheduleField.Time);
            if (zone.IsAmbiguousTime(wallTime))
                return Problem($"{selected} happens twice because the clocks move back. Choose another time or use UTC.", ScheduleField.Time);
            utc = TimeZoneInfo.ConvertTimeToUtc(wallTime, zone);
        }
        else utc = DateTime.SpecifyKind(wallTime, DateTimeKind.Utc);
        if (utc > nowUtc) return new(utc, null);

        DateTimeOffset now = choice.Local ? TimeZoneInfo.ConvertTime(new DateTimeOffset(nowUtc), zone) : new DateTimeOffset(nowUtc);
        string nowText = $"{DateDisplay.Timestamp(now, format, seconds: false)} {zoneName}";
        return date.Date < now.Date
            ? Problem($"{selected} is in the past. It is now {nowText}; choose today or a later date.", ScheduleField.Date)
            : Problem($"{selected} has already passed. It is now {nowText}; choose a later time.", ScheduleField.Time);
    }

    private static ScheduleResolution Problem(string message, ScheduleField field) => new(default, new(message, field));

    /// <summary>The label of the action that takes the user to the field to change.</summary>
    public static string ActionFor(ScheduleProblem? problem) => problem?.Field == ScheduleField.Time ? "Change time" : "Change date";

    /// <summary>
    /// The first day the calendar offers. A UTC schedule may still be today in UTC after the local date has moved on,
    /// and vice versa, so it is the earlier of the two; an already selected past day stays in range so it is shown next
    /// to the notice that explains it.
    /// </summary>
    public static DateTime EarliestDay(DateTime localToday, DateTime nowUtc, DateTime? selected)
    {
        DateTime earliest = localToday < nowUtc.Date ? localToday : nowUtc.Date;
        return selected is DateTime day && day.Date < earliest ? day.Date : earliest;
    }
}
