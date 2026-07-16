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
}
