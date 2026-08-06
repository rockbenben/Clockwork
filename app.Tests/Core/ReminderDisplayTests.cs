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

    [Fact]
    public void Period_once_shows_date()
        => Assert.Equal("仅一次 2026-08-01", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "once", OnceDate = "2026-08-01" }));

    [Fact]
    public void Period_once_without_date()
        => Assert.Equal("仅一次", ReminderDisplay.PeriodLabel(new Reminder { RecurType = "once", OnceDate = "" }));

    [Fact]
    public void Time_shows_interval_suffix()
        => Assert.Equal("09:00 每 30 分钟 至 18:00", ReminderDisplay.TimeLabel(new Reminder { Time = "09:00", IntervalMinutes = 30, IntervalUntil = "18:00" }));

    [Fact]
    public void Time_interval_without_until()
        => Assert.Equal("09:00 每 30 分钟", ReminderDisplay.TimeLabel(new Reminder { Time = "09:00", IntervalMinutes = 30 }));

    [Fact]
    public void Period_startup_trigger_is_each_login()
        => Assert.Equal("每次登录", ReminderDisplay.PeriodLabel(new Reminder { Trigger = "startup", RecurType = "daily" }));

    [Fact]
    public void Period_startup_ignores_recur_type()
        => Assert.Equal("每次登录", ReminderDisplay.PeriodLabel(new Reminder { Trigger = "startup", RecurType = "monthly", MonthlyDay = 5 }));

    [Fact]
    public void Text_silent_task_shows_group_name()
    {
        var groups = new List<ActionGroup> { new() { Id = "g1", Name = "专注·开始工作" } };
        var r = new Reminder { SilentGroupId = "g1", Message = "" };
        Assert.Equal("运行动作组：专注·开始工作", ReminderDisplay.TextSummary(r, groups));
    }

    [Fact]
    public void Text_silent_task_with_missing_group_says_none()
    {
        var r = new Reminder { SilentGroupId = "gone", Message = "" };
        Assert.Equal("运行动作组：（未指定）", ReminderDisplay.TextSummary(r, new List<ActionGroup>()));
    }

    [Fact]
    public void Text_normal_task_still_shows_message()
        => Assert.Equal("喝水", ReminderDisplay.TextSummary(new Reminder { Message = "喝水" }));
}
