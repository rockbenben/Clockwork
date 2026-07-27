using Clockwork.Core;
using Xunit;

public class ReminderDecisionTests
{
    private static DateTime D(int h, int m, int s = 0) => new DateTime(2026, 7, 15, h, m, s); // 周三

    [Fact]
    public void Disabled_none()
        => Assert.Equal("none", ReminderEngine.Decide(new Reminder { Enabled = false }, D(10, 0), D(9, 0), new ReminderState()).Action);

    [Fact]
    public void Time_arms_within_grace()
    {
        var r = new Reminder { Trigger = "time", Time = "10:00", GraceMinutes = 5, Days = new() };
        var d = ReminderEngine.Decide(r, D(10, 3, 30), D(9, 0), new ReminderState());
        Assert.Equal("arm", d.Action);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), d.Base);
    }

    [Fact]
    public void Time_single_digit_hour_accepted()
    {
        // 手改 json 写 "9:00"（单位数小时）：宽容解析，照常 arm——不再静默永不触发。
        var r = new Reminder { Trigger = "time", Time = "9:00", GraceMinutes = 5, Days = new() };
        var d = ReminderEngine.Decide(r, D(9, 2), D(8, 0), new ReminderState());
        Assert.Equal("arm", d.Action);
        Assert.Equal(new DateTime(2026, 7, 15, 9, 0, 0), d.Base);
    }

    [Fact]
    public void Time_past_grace_none()
    {
        var r = new Reminder { Trigger = "time", Time = "10:00", GraceMinutes = 5, Days = new() };
        Assert.Equal("none", ReminderEngine.Decide(r, D(10, 6), D(9, 0), new ReminderState()).Action);
    }

    [Fact]
    public void Time_not_fired_twice_same_day()
    {
        var r = new Reminder { Trigger = "time", Time = "10:00", GraceMinutes = 5, Days = new() };
        var st = new ReminderState { LastFiredDate = "2026-07-15" };
        Assert.Equal("none", ReminderEngine.Decide(r, D(10, 1), D(9, 0), st).Action);
    }

    [Fact]
    public void Pending_fires_when_due()
    {
        var r = new Reminder { Trigger = "time", Time = "10:00", Days = new() };
        var st = new ReminderState { PendingFireAt = D(10, 2) };
        var d = ReminderEngine.Decide(r, D(10, 2), D(9, 0), st);
        Assert.Equal("fire", d.Action);
        Assert.Null(st.PendingFireAt);
        Assert.Equal("2026-07-15", st.LastFiredDate);
    }

    [Fact]
    public void Snooze_fires_when_due_even_off_recurrence()
    {
        var r = new Reminder { Trigger = "time", Time = "10:00", Days = new() { 6 } }; // 周六限制，今天周三
        var st = new ReminderState { SnoozeUntil = D(10, 0) };
        Assert.Equal("fire", ReminderEngine.Decide(r, D(10, 0), D(9, 0), st).Action);
        Assert.Null(st.SnoozeUntil);
    }

    [Fact]
    public void Pending_survives_midnight_into_off_recurrence_day()
    {
        // 周三限定 23:58 + 延迟推过午夜：周四 00:01 到点仍应触发——arm 发生在有效周期日，
        // 已武装的一次触发不该被次日的周期过滤抹掉（与 snooze/repeat 同待遇）。
        var r = new Reminder { Trigger = "time", Time = "23:58", Days = new() { 3 } }; // 仅周三；7/16 是周四
        var st = new ReminderState { PendingFireAt = new DateTime(2026, 7, 16, 0, 1, 0) };
        var d = ReminderEngine.Decide(r, new DateTime(2026, 7, 16, 0, 1, 30), D(9, 0), st);
        Assert.Equal("fire", d.Action);
        Assert.Null(st.PendingFireAt);
        Assert.Equal("2026-07-16", st.LastFiredDate);
    }

    [Fact]
    public void Pending_not_yet_due_waits_across_midnight()
    {
        var r = new Reminder { Trigger = "time", Time = "23:58", Days = new() { 3 } };
        var st = new ReminderState { PendingFireAt = new DateTime(2026, 7, 16, 0, 5, 0) };
        Assert.Equal("none", ReminderEngine.Decide(r, new DateTime(2026, 7, 16, 0, 1, 0), D(9, 0), st).Action);
        Assert.NotNull(st.PendingFireAt);   // 仍在等，不被周期过滤清掉
    }

    [Fact]
    public void Stale_pending_fires_once_on_wake_not_dropped()
    {
        // 周五武装后合盖休眠到下周一：唤醒后晚发一次（旧版行为）。曾加过「过期跨日丢弃」守卫又撤销——
        // 丢弃会造成三种静默丢失：「登录时」武装/丢弃死循环、23:55 武装跨午夜整周丢失、多天错峰延时被一再顺延。
        var r = new Reminder { Trigger = "time", Time = "22:00", Days = new() { 5 } };   // 仅周五
        var st = new ReminderState { PendingFireAt = new DateTime(2026, 7, 10, 22, 10, 0) };   // 上周五
        var d = ReminderEngine.Decide(r, new DateTime(2026, 7, 13, 9, 0, 0), D(8, 0), st);      // 周一早晨唤醒
        Assert.Equal("fire", d.Action);
        Assert.Null(st.PendingFireAt);
    }

    [Fact]
    public void Stale_snooze_before_today_dropped_not_fired()
    {
        // 跨日停机后载入的过期"稍后"(昨天 07-14)：不补弹、清掉，继续正常判定（周六限定+今天周三 → none）。
        var r = new Reminder { Trigger = "time", Time = "10:00", Days = new() { 6 } };
        var st = new ReminderState { SnoozeUntil = new DateTime(2026, 7, 14, 10, 0, 0) };
        Assert.Equal("none", ReminderEngine.Decide(r, D(9, 0), D(9, 0), st).Action);
        Assert.Null(st.SnoozeUntil);
    }

    [Fact]
    public void Stale_snooze_with_catchup_fires_once()
    {
        // 「错过必补」例外：挂着的稍后（含无人应答的自动稍后）跨天后补发一次——即使今天不在周期日
        // （周六限定+今天周三照补，与到点稍后的越周期语义一致），且只补这一次（SnoozeUntil 已清）。
        var r = new Reminder { Trigger = "time", Time = "10:00", Days = new() { 6 }, CatchUpIfMissed = true };
        var st = new ReminderState { SnoozeUntil = new DateTime(2026, 7, 14, 23, 41, 0) };
        Assert.Equal("fire", ReminderEngine.Decide(r, D(9, 0), D(9, 0), st).Action);
        Assert.Null(st.SnoozeUntil);
        // 第二次判定不再补：稍后已清，回到正常周期判定（周六限定 → none）。
        Assert.Equal("none", ReminderEngine.Decide(r, D(9, 1), D(9, 0), st).Action);
    }

    [Fact]
    public void Startup_arms_when_fresh()
    {
        var r = new Reminder { Trigger = "startup", StartupWithinMinutes = 10, StartupHourMode = "any" };
        var d = ReminderEngine.Decide(r, D(9, 1), D(9, 0), new ReminderState(), uptimeMinutes: 2);
        Assert.Equal("arm", d.Action);
    }

    [Fact]
    public void Startup_skipped_when_uptime_exceeds_window()
    {
        var r = new Reminder { Trigger = "startup", StartupWithinMinutes = 10 };
        var st = new ReminderState();
        Assert.Equal("none", ReminderEngine.Decide(r, D(9, 1), D(9, 0), st, uptimeMinutes: 30).Action);
        Assert.True(st.StartupHandled);
    }

    [Fact]
    public void Pending_fires_even_off_recurrence()
    {
        // 与 snooze 同待遇：arm 只发生在有效周期日，已武装的待发跨到非周期日也照发一次（旧行为是清掉不发——那是 bug：
        // 周五 23:58 + 延迟推过午夜会被周六的周期过滤抹掉）。
        var r = new Reminder { Trigger = "time", Time = "10:00", Days = new() { 6 } }; // 今天周三非周六
        var st = new ReminderState { PendingFireAt = D(10, 0) };
        Assert.Equal("fire", ReminderEngine.Decide(r, D(10, 0), D(9, 0), st).Action);
        Assert.Null(st.PendingFireAt);
        Assert.Equal("2026-07-15", st.LastFiredDate);
    }

    [Fact]
    public void CatchUp_fires_when_existed_at_startup()
    {
        // 启动时就存在的提醒(existedAtStartup=true，默认)，因休眠/关机错过 09:00 → 14:00 tick 补弹。
        var r = new Reminder { Trigger = "time", Time = "09:00", GraceMinutes = 5, CatchUpIfMissed = true, Days = new() };
        Assert.Equal("arm", ReminderEngine.Decide(r, D(14, 0), D(7, 0), new ReminderState()).Action);
    }

    [Fact]
    public void CatchUp_not_fired_if_created_midsession()
    {
        // 到点后才新建的提醒(existedAtStartup=false)：14:00 tick 不立刻补弹。
        var r = new Reminder { Trigger = "time", Time = "09:00", GraceMinutes = 5, CatchUpIfMissed = true, Days = new() };
        Assert.Equal("none", ReminderEngine.Decide(r, D(14, 0), D(7, 0), new ReminderState(), existedAtStartup: false).Action);
    }

    [Fact]
    public void CatchUp_not_fired_twice_same_day()
    {
        var r = new Reminder { Trigger = "time", Time = "09:00", CatchUpIfMissed = true, Days = new() };
        var st = new ReminderState { LastFiredDate = "2026-07-15" };
        Assert.Equal("none", ReminderEngine.Decide(r, D(14, 0), D(7, 0), st).Action);
    }

    [Fact]
    public void No_catchup_past_grace_none()
    {
        // 对照：未开补弹，过了 grace → none（默认行为不变）。
        var r = new Reminder { Trigger = "time", Time = "09:00", GraceMinutes = 5, Days = new() };
        Assert.Equal("none", ReminderEngine.Decide(r, D(14, 0), D(7, 0), new ReminderState()).Action);
    }

    [Fact]
    public void Repeat_continues_across_midnight_off_recurrence()
    {
        // 周五限定、23:50、跨午夜重复。在途 NextRepeatAt 落到周六凌晨——应照发（延续上一次有效触发），
        // 不被"非周六"的周期过滤清掉。2026-07-17=周五, 07-18=周六。
        var r = new Reminder { Trigger = "time", Time = "23:50", Days = new() { 5 }, RepeatMinutes = 15 };
        var st = new ReminderState { NextRepeatAt = new DateTime(2026, 7, 18, 0, 5, 0), RepeatCount = 1 };
        var d = ReminderEngine.Decide(r, new DateTime(2026, 7, 18, 0, 5, 0), new DateTime(2026, 7, 17, 23, 50, 0), st);
        Assert.Equal("fire", d.Action);
        Assert.Null(st.NextRepeatAt);
    }

    [Fact]
    public void Interval_fires_even_after_fired_today()
    {
        // 循环轮次不被「今天已弹过」挡掉——interval 分支在 LastFiredDate 判断之前。
        var r = new Reminder { Trigger = "time", Time = "09:00", IntervalMinutes = 30, Days = new() };
        var st = new ReminderState { LastFiredDate = "2026-07-15", NextIntervalAt = D(10, 0) };
        var d = ReminderEngine.Decide(r, D(10, 0), D(8, 0), st);
        Assert.Equal("fire", d.Action);
        Assert.Null(st.NextIntervalAt);
    }

    [Fact]
    public void Snooze_beats_interval()
    {
        var r = new Reminder { Trigger = "time", Time = "09:00", IntervalMinutes = 30, Days = new() };
        var st = new ReminderState { SnoozeUntil = D(10, 0), NextIntervalAt = D(10, 0) };
        var d = ReminderEngine.Decide(r, D(10, 0), D(8, 0), st);
        Assert.Equal("fire", d.Action);
        Assert.Null(st.SnoozeUntil);              // 消耗的是 snooze
        Assert.Equal(D(10, 0), st.NextIntervalAt); // interval 原样留着
    }

    [Fact]
    public void Repeat_beats_interval()
    {
        var r = new Reminder { Trigger = "time", Time = "09:00", RepeatMinutes = 5, IntervalMinutes = 30, Days = new() };
        var st = new ReminderState { NextRepeatAt = D(10, 0), NextIntervalAt = D(10, 0) };
        var d = ReminderEngine.Decide(r, D(10, 0), D(8, 0), st);
        Assert.Equal("fire", d.Action);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(D(10, 0), st.NextIntervalAt);
    }

    [Fact]
    public void Stale_crossday_interval_discarded_catchup_does_not_revive_it()
    {
        // 昨天的轮次不补（漏掉的轮询没有补发价值）；但当天正常首发照走——错过必补作用于 base 时刻，不作用于轮次。
        var r = new Reminder { Trigger = "time", Time = "09:00", IntervalMinutes = 30, CatchUpIfMissed = true, Days = new() };
        var st = new ReminderState { LastFiredDate = "2026-07-14", NextIntervalAt = new DateTime(2026, 7, 14, 18, 0, 0) };
        var d = ReminderEngine.Decide(r, D(11, 0), D(8, 0), st);
        Assert.Null(st.NextIntervalAt);        // 陈旧轮次丢弃
        Assert.Equal("arm", d.Action);         // 当天 09:00 首发经错过必补正常武装
        Assert.Equal(new DateTime(2026, 7, 15, 9, 0, 0), d.Base);
    }

    [Fact]
    public void Sameday_overdue_interval_fires()
    {
        // 休眠唤醒后当天已过期的轮次：到点即发一次（与 snooze 的 now>=snooze 分支同型）。
        var r = new Reminder { Trigger = "time", Time = "09:00", IntervalMinutes = 30, Days = new() };
        var st = new ReminderState { LastFiredDate = "2026-07-15", NextIntervalAt = D(10, 0) };
        Assert.Equal("fire", ReminderEngine.Decide(r, D(11, 30), D(8, 0), st).Action);
    }
}
