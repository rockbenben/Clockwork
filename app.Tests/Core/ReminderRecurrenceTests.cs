using Clockwork.Core;
using Xunit;

public class ReminderRecurrenceTests
{
    [Fact]
    public void Daily_empty_days_due_everyday()
        => Assert.True(ReminderEngine.IsRecurrenceDueToday(new Reminder { RecurType = "daily", Days = new() }, new DateTime(2026, 7, 15)));

    [Fact]
    public void Daily_weekday_filter()
    {
        var r = new Reminder { RecurType = "daily", Days = new() { 1, 2, 3, 4, 5 } };
        Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 15)));  // 周三
        Assert.False(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 18))); // 周六
    }

    [Fact]
    public void EveryNDays_from_anchor()
    {
        var r = new Reminder { RecurType = "everyNDays", IntervalDays = 3, AnchorDate = "2026-07-15" };
        Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 15)));  // 第0天
        Assert.False(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 16))); // 第1天
        Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 7, 18)));  // 第3天
    }

    [Fact]
    public void EveryNDays_before_anchor_false()
        => Assert.False(ReminderEngine.IsRecurrenceDueToday(new Reminder { RecurType = "everyNDays", IntervalDays = 2, AnchorDate = "2026-07-15" }, new DateTime(2026, 7, 14)));

    [Fact]
    public void Monthly_clamps_to_month_end()
    {
        var r = new Reminder { RecurType = "monthly", MonthlyDay = 31 };
        Assert.True(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 2, 28)));  // 2月夹到 28
        Assert.False(ReminderEngine.IsRecurrenceDueToday(r, new DateTime(2026, 2, 27)));
    }

    [Theory]
    [InlineData("before", 9, 8, true)]
    [InlineData("before", 9, 9, false)]
    [InlineData("after", 9, 9, true)]
    [InlineData("after", 9, 8, false)]
    [InlineData("any", 9, 8, true)]
    public void StartupHourOk(string mode, int startupHour, int loginHour, bool ok)
        => Assert.Equal(ok, ReminderEngine.IsStartupHourOk(new Reminder { StartupHourMode = mode, StartupHour = startupHour }, new DateTime(2026, 7, 15, loginHour, 0, 0)));

    [Fact] public void PopupTimeout_explicit_wins() => Assert.Equal(15, ReminderEngine.PopupTimeoutSeconds(new Reminder { PopupTimeoutSeconds = 15 }));
    [Fact] public void PopupTimeout_repeat_default_60() => Assert.Equal(60, ReminderEngine.PopupTimeoutSeconds(new Reminder { RepeatMinutes = 5 }));
    [Fact] public void PopupTimeout_none_zero() => Assert.Equal(0, ReminderEngine.PopupTimeoutSeconds(new Reminder()));
}
