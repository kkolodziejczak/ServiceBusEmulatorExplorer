using ServiceBusEmulatorExplorer.Core.Investigation;
using ServiceBusEmulatorExplorer.Presentation.Workbench;

namespace ServiceBusEmulatorExplorer.Presentation.Tests;

public sealed class WorkbenchScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// UTC+3, with an hour of summer time from the last Sunday of March 04:00 to the last Sunday of October 05:00. Its
    /// clocks change at hours the machine's own zone does not, so a schedule read in the wrong zone fails these tests.
    /// </summary>
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Workbench test zone", TimeSpan.FromHours(3),
        "Workbench test zone", "Workbench standard", "Workbench summer",
        [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 4, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 5, 0, 0), 10, 5, DayOfWeek.Sunday))]);

    [Fact]
    public void Sending_now_can_be_confirmed_and_says_where_the_messages_go()
    {
        Assert.Equal(new ReviewTiming("Send 1 message", "This message will be sent to the queue.", true, null, null),
            WorkbenchSchedule.SendNow(1, queue: true));
        Assert.Equal(new ReviewTiming("Send 3 messages", "3 messages will be sent to the queue.", true, null, null),
            WorkbenchSchedule.SendNow(3, queue: true));
        Assert.Equal(new ReviewTiming("Send 1 message", "Subscriptions receive messages according to their rules.", true, null, null),
            WorkbenchSchedule.SendNow(1, queue: false));
    }

    [Fact]
    public void A_future_utc_schedule_resolves_to_one_instant_for_every_message()
    {
        var timing = WorkbenchSchedule.Schedule(3, Utc(new(2026, 10, 19), "14", "30"), Now, Zone, DateDisplayFormat.Iso);

        Assert.Equal(new ReviewTiming("Schedule 3 messages", "3 sample messages will be scheduled for the same instant.", true, null,
            "Resolved time: 2026-10-19 14:30 UTC · one time for all messages"), timing);
        Assert.Equal("1 sample message will be scheduled for the same instant.",
            WorkbenchSchedule.Schedule(1, Utc(new(2026, 10, 19), "14", "30"), Now, Zone, DateDisplayFormat.Iso).Notice);
        Assert.Equal(new ScheduleResolution(new DateTime(2026, 10, 18, 9, 31, 0, DateTimeKind.Utc), null),
            WorkbenchSchedule.Resolve(Utc(new(2026, 10, 18), "09", "31"), Now, Zone, DateDisplayFormat.Iso));
    }

    [Fact]
    public void A_schedule_without_a_day_or_a_valid_time_names_the_field_to_change()
    {
        AssertProblem("Choose a date for the scheduled send.", ScheduleField.Date, new(null, "14", "30", false));
        foreach (var (hour, minute) in new[] { ((string?)null, (string?)"30"), ("14", null), ("ab", "30"), ("+1", "30"), ("24", "00"), ("14", "60") })
            AssertProblem("Choose an hour and a minute for the scheduled send.", ScheduleField.Time, new(new(2026, 10, 19), hour, minute, false));

        var timing = WorkbenchSchedule.Schedule(2, new(null, "14", "30", false), Now, Zone, DateDisplayFormat.Iso);
        Assert.False(timing.CanConfirm);
        Assert.Equal("One time for all messages", timing.ResolvedText);
        Assert.Equal("Schedule 2 messages", timing.ConfirmText);
    }

    [Fact]
    public void A_past_utc_schedule_says_whether_the_day_or_the_time_has_passed()
    {
        AssertProblem("2026-10-17 14:30 UTC is in the past. It is now 2026-10-18 09:30 UTC; choose today or a later date.",
            ScheduleField.Date, Utc(new(2026, 10, 17), "14", "30"));
        AssertProblem("2026-10-18 09:30 UTC has already passed. It is now 2026-10-18 09:30 UTC; choose a later time.",
            ScheduleField.Time, Utc(new(2026, 10, 18), "09", "30"));
        AssertProblem("18/10/2026 08:00 UTC has already passed. It is now 18/10/2026 09:30 UTC; choose a later time.",
            ScheduleField.Time, Utc(new(2026, 10, 18), "08", "00"), DateDisplayFormat.DayFirst);
    }

    [Fact]
    public void A_local_schedule_is_read_in_the_given_zone()
    {
        Assert.Equal(new ScheduleResolution(new DateTime(2026, 10, 19, 10, 30, 0, DateTimeKind.Utc), null),
            WorkbenchSchedule.Resolve(Local(new(2026, 10, 19), "14", "30"), Now, Zone, DateDisplayFormat.Iso));
        Assert.Equal(new ScheduleResolution(new DateTime(2026, 11, 2, 11, 30, 0, DateTimeKind.Utc), null),
            WorkbenchSchedule.Resolve(Local(new(2026, 11, 2), "14", "30"), Now, Zone, DateDisplayFormat.Iso));

        AssertProblem("2026-03-29 04:30 local time does not exist because the clocks move forward. Choose another time or use UTC.",
            ScheduleField.Time, Local(new(2026, 3, 29), "04", "30"));
        AssertProblem("2026-10-25 04:30 local time happens twice because the clocks move back. Choose another time or use UTC.",
            ScheduleField.Time, Local(new(2026, 10, 25), "04", "30"));
        AssertProblem("2026-10-18 13:00 local time has already passed. It is now 2026-10-18 13:30 local time; choose a later time.",
            ScheduleField.Time, Local(new(2026, 10, 18), "13", "00"));

        var lateEvening = new DateTime(2026, 10, 18, 21, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new ScheduleProblem(
                "2026-10-18 23:00 local time is in the past. It is now 2026-10-19 01:30 local time; choose today or a later date.", ScheduleField.Date),
            WorkbenchSchedule.Resolve(Local(new(2026, 10, 18), "23", "00"), lateEvening, Zone, DateDisplayFormat.Iso).Problem);
        Assert.Null(WorkbenchSchedule.Resolve(Utc(new(2026, 10, 18), "23", "00"), lateEvening, Zone, DateDisplayFormat.Iso).Problem);
    }

    [Fact]
    public void The_problem_action_goes_to_the_time_or_otherwise_the_date()
    {
        Assert.Equal("Change time", WorkbenchSchedule.ActionFor(new("", ScheduleField.Time)));
        Assert.Equal("Change date", WorkbenchSchedule.ActionFor(new("", ScheduleField.Date)));
        Assert.Equal("Change date", WorkbenchSchedule.ActionFor(null));
    }

    [Fact]
    public void The_calendar_starts_at_the_earlier_of_today_in_utc_and_locally_or_at_a_selected_past_day()
    {
        Assert.Equal(new DateTime(2026, 10, 18), WorkbenchSchedule.EarliestDay(new(2026, 10, 19), Now, null));
        Assert.Equal(new DateTime(2026, 10, 17), WorkbenchSchedule.EarliestDay(new(2026, 10, 17), Now, new DateTime(2026, 10, 20)));
        Assert.Equal(new DateTime(2026, 10, 12), WorkbenchSchedule.EarliestDay(new(2026, 10, 18), Now, new DateTime(2026, 10, 12, 15, 0, 0)));
    }

    private static ScheduleChoice Utc(DateTime date, string hour, string minute) => new(date, hour, minute, Local: false);

    private static ScheduleChoice Local(DateTime date, string hour, string minute) => new(date, hour, minute, Local: true);

    private static void AssertProblem(string message, ScheduleField field, ScheduleChoice choice, DateDisplayFormat format = DateDisplayFormat.Iso)
    {
        Assert.Equal(new ScheduleResolution(default, new(message, field)), WorkbenchSchedule.Resolve(choice, Now, Zone, format));
        var timing = WorkbenchSchedule.Schedule(3, choice, Now, Zone, format);
        Assert.Equal(new ScheduleProblem(message, field), timing.Problem);
        Assert.False(timing.CanConfirm);
    }
}
