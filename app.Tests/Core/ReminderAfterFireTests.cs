using Clockwork.Core;
using Xunit;

public class ReminderAfterFireTests
{
    private static DateTime N(int h, int m) => new DateTime(2026, 7, 15, h, m, 0);

    [Fact]
    public void Confirmed_stops_repeat()
    {
        var st = new ReminderState { RepeatCount = 3, NextRepeatAt = N(10, 5) };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 5 }, N(10, 0), "yes", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(0, st.RepeatCount);
    }

    [Fact]
    public void No_repeat_config_clears()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 0 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void Unconfirmed_schedules_next()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 10 }, N(10, 0), "", st);
        Assert.Equal(N(10, 10), st.NextRepeatAt);
        Assert.Equal(1, st.RepeatCount);
    }

    [Fact]
    public void RepeatUntil_stops_past_deadline()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 30, RepeatUntil = "10:20" }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);   // 10:30 > 10:20
    }

    [Fact]
    public void RepeatUntil_single_digit_hour_still_enforced()
    {
        // 手改 json 的 repeatUntil="9:30"：规整后照常生效，不再因过不了严格校验而整个截止判定被跳过。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "09:00", RepeatMinutes = 30, RepeatUntil = "9:20" }, N(9, 0), "", st);
        Assert.Null(st.NextRepeatAt);   // 9:30 > 09:20 → 停
    }

    [Fact]
    public void RepeatUntil_not_extended_for_single_digit_hour_time()
    {
        // 手改 json 的 time="9:00"：序数比较 "10:30"<"9:00" 会把当天已过的截止误判成跨午夜、顺延一天，
        // 催促窗被错误拉长 ~7 小时。比较前须规整 Time。
        var st = new ReminderState();
        var r = new Reminder { Time = "9:00", RepeatMinutes = 20, RepeatUntil = "10:30" };
        ReminderEngine.UpdateAfterFire(r, new DateTime(2026, 7, 15, 11, 1, 0), "", st);   // 11:01 触发（错过必补场景）
        Assert.Null(st.NextRepeatAt);   // 截止 10:30 已过 → 停，不顺延到明天
    }

    [Fact]
    public void RepeatUntil_crossing_midnight_still_schedules()
    {
        // 23:50 触发、每 15 分钟、截止 00:30(早于提醒时刻→跨午夜)：下一次 00:05 仍应排上。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "23:50", RepeatMinutes = 15, RepeatUntil = "00:30" }, N(23, 50), "", st);
        Assert.Equal(new DateTime(2026, 7, 16, 0, 5, 0), st.NextRepeatAt);
    }

    [Fact]
    public void RepeatUntil_crossing_midnight_stops_after_window()
    {
        // 次日 00:20 触发、下一次 00:35 已越过当日 00:30 截止 → 停。
        var st = new ReminderState { RepeatCount = 2 };
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "23:50", RepeatMinutes = 15, RepeatUntil = "00:30" }, new DateTime(2026, 7, 16, 0, 20, 0), "", st);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void RepeatUntil_elapsed_same_day_stops()
    {
        // 提醒 10:15、截止 10:20(晚于提醒时刻，非跨午夜)，触发被延时推到 10:21：不得误判为次日，应停。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { Time = "10:15", RepeatMinutes = 15, RepeatUntil = "10:20" }, N(10, 21), "", st);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void MaxRepeats_caps()
    {
        var st = new ReminderState { RepeatCount = ReminderEngine.MaxRepeats - 1 };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 5 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(0, st.RepeatCount);
    }

    [Fact]
    public void Snooze_sets_and_clears_repeat()
    {
        var st = new ReminderState { NextRepeatAt = N(10, 5) };
        ReminderEngine.Snooze(st, N(10, 0), 15);
        Assert.Equal(N(10, 15), st.SnoozeUntil);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void Snooze_under_1_defaults_10()
    {
        var st = new ReminderState();
        ReminderEngine.Snooze(st, N(10, 0), 0);
        Assert.Equal(N(10, 10), st.SnoozeUntil);
    }
}
