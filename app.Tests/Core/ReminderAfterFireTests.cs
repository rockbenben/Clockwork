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
        // 同时配了循环：过截止是催促链的一个出口，出口上必须接着排下一轮循环——
        // 否则「催促 + 循环」的任务在催促窗结束后就静默丢掉当天余下的所有轮次。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 30, RepeatUntil = "10:20", IntervalMinutes = 30 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);            // 10:30 > 10:20
        Assert.Equal(N(10, 30), st.NextIntervalAt);
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
        // 同时配了循环：达催促上限也是催促链的一个出口，同样要排下一轮循环（理由见 RepeatUntil_stops_past_deadline）。
        var st = new ReminderState { RepeatCount = ReminderEngine.MaxRepeats - 1 };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 5, IntervalMinutes = 30 }, N(10, 0), "", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(0, st.RepeatCount);
        Assert.Equal(N(10, 30), st.NextIntervalAt);
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

    [Fact]
    public void Confirm_schedules_next_interval()
    {
        // 确认不终止循环——这正是它与催促的区别（静默组固定返回 "ok"，静默任务的轮询靠这条路径成立）。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30 }, N(10, 0), "ok", st);
        Assert.Equal(N(10, 30), st.NextIntervalAt);
    }

    [Fact]
    public void Ongoing_nag_chain_defers_interval()
    {
        // 催促链在途（NextRepeatAt 排上了）→ 不排 interval，两条链不互相插队。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 10, IntervalMinutes = 30 }, N(10, 0), "", st);
        Assert.Equal(N(10, 10), st.NextRepeatAt);
        Assert.Null(st.NextIntervalAt);
    }

    [Fact]
    public void Nag_chain_end_schedules_interval()
    {
        // 催促确认收尾的那一刻排下一轮循环。
        var st = new ReminderState { RepeatCount = 2, NextRepeatAt = N(10, 5) };
        ReminderEngine.UpdateAfterFire(new Reminder { RepeatMinutes = 10, IntervalMinutes = 30 }, N(10, 6), "yes", st);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(N(10, 36), st.NextIntervalAt);
    }

    [Fact]
    public void Interval_until_stops_for_the_day()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30, IntervalUntil = "10:20" }, N(10, 0), "ok", st);
        Assert.Null(st.NextIntervalAt);   // 10:30 > 10:20
    }

    [Fact]
    public void Interval_until_boundary_is_inclusive()
    {
        // 正好落在截止时刻的那一轮必须照排：判定是 next > until。10:30 vs 10:20 的用例分不出 > 与 >=，
        // 而改成 >= 会静默砍掉每个窗口最后一轮合法运行。
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30, IntervalUntil = "10:30" }, N(10, 0), "ok", st);
        Assert.Equal(N(10, 30), st.NextIntervalAt);
    }

    [Fact]
    public void Interval_defaults_to_end_of_day()
    {
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(new Reminder { IntervalMinutes = 30, IntervalUntil = "" }, new DateTime(2026, 7, 15, 23, 40, 0), "ok", st);
        Assert.Null(st.NextIntervalAt);   // 00:10 越过当天 23:59 → 本日循环结束，不跨午夜
    }

    [Fact]
    public void Once_disables_only_after_chains_end()
    {
        var r = new Reminder { RecurType = "once" };
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState()));                                        // 还没弹过
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState { LastFiredDate = "2026-07-15", SnoozeUntil = N(10, 10) }));   // 稍后在途
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState { LastFiredDate = "2026-07-15", NextRepeatAt = N(10, 5) }));   // 催促在途
        Assert.True(ReminderEngine.ShouldDisableAfterOnce(r, new ReminderState { LastFiredDate = "2026-07-15" }));           // 链清 → 停用
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(new Reminder { RecurType = "daily" }, new ReminderState { LastFiredDate = "2026-07-15" }));
    }
}
