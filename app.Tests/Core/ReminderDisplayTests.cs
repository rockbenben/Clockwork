using Clockwork.Core;
using Xunit;

public class ReminderDisplayTests
{
    [Fact] public void Time_trigger_shows_time() => Assert.Equal("10:00", ReminderDisplay.TimeLabel(new Reminder { Trigger = "time", Time = "10:00" }));
    [Fact] public void Startup_before() => Assert.Equal("登录时·9点前", ReminderDisplay.TimeLabel(new Reminder { Trigger = "startup", StartupHourMode = "before", StartupHour = 9 }));
    [Fact] public void Startup_after() => Assert.Equal("登录时·9点后", ReminderDisplay.TimeLabel(new Reminder { Trigger = "startup", StartupHourMode = "after", StartupHour = 9 }));
    [Fact] public void Startup_any() => Assert.Equal("登录时", ReminderDisplay.TimeLabel(new Reminder { Trigger = "startup", StartupHourMode = "any" }));

    [Fact] public void Period_everyNDays() => Assert.Equal("每3天", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "everyNDays", IntervalDays = 3 }));
    [Fact] public void Period_monthly() => Assert.Equal("每月15号", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "monthly", MonthlyDay = 15 }));
    [Fact] public void Period_daily_weekdays() => Assert.Equal("一二三四五", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "daily", Days = new() { 1, 2, 3, 4, 5 } }));
    [Fact] public void Period_daily_empty_everyday() => Assert.Equal("每天", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "daily", Days = new() }));

    [Fact] public void Text_strips_newlines() => Assert.Equal("a b", ReminderDisplay.TextSummary(new Reminder { Message = "a\r\nb" }));
}
