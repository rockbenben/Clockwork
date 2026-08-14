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

    // 「登录时」不受周期约束。编辑器把整块周期 UI 对它隐藏、却为往返保真原样存回旧值，
    // 所以「把一条每月/仅一次的提醒改成登录时」之后残留值一定还在——运行期再照它过滤，
    // 这条提醒就被静默钉死（改自过期的 once 则永远不再触发），而列表上一直写着「每次登录」。
    [Fact]
    public void Startup_ignores_stale_monthly_recurrence()
    {
        // 今天是 15 号，残留的 monthly 指向 1 号：过滤若生效则今天不触发。
        var r = new Reminder { Trigger = "startup", RecurType = "monthly", MonthlyDay = 1, Days = new() };
        Assert.Equal("arm", ReminderEngine.Decide(r, D(10, 0), D(9, 55), new ReminderState()).Action);
    }

    [Fact]
    public void Startup_ignores_stale_expired_once()
    {
        var r = new Reminder { Trigger = "startup", RecurType = "once", OnceDate = "2020-01-01", Days = new() };
        Assert.Equal("arm", ReminderEngine.Decide(r, D(10, 0), D(9, 55), new ReminderState()).Action);
    }

    [Fact]
    public void Startup_ignores_stale_weekday_filter()
    {
        // 2026-07-15 是周三(3)；残留的星期限制只勾了周一(1)。
        var r = new Reminder { Trigger = "startup", RecurType = "daily", Days = new() { 1 } };
        Assert.Equal("arm", ReminderEngine.Decide(r, D(10, 0), D(9, 55), new ReminderState()).Action);
    }

    [Fact]
    public void Time_trigger_still_honours_recurrence()   // 别把周期过滤整个改没了
    {
        var r = new Reminder { Trigger = "time", Time = "10:00", RecurType = "monthly", MonthlyDay = 1, Days = new() };
        Assert.Equal("none", ReminderEngine.Decide(r, D(10, 1), D(9, 0), new ReminderState()).Action);
    }

    // 残留的 once 也不该让「登录时」提醒响一次就被自动取消勾选。
    [Fact]
    public void Startup_with_stale_once_is_not_auto_disabled()
    {
        var r = new Reminder { Trigger = "startup", RecurType = "once", Enabled = true };
        var st = new ReminderState { LastFiredDate = "2026-07-15" };
        Assert.False(ReminderEngine.ShouldDisableAfterOnce(r, st));
    }

    [Fact]
    public void Time_with_once_is_still_auto_disabled()
    {
        var r = new Reminder { Trigger = "time", RecurType = "once", Enabled = true };
        var st = new ReminderState { LastFiredDate = "2026-07-15" };
        Assert.True(ReminderEngine.ShouldDisableAfterOnce(r, st));
    }

    // 跨午夜引爆要记「为哪一天准备的」，不是「哪一天引爆的」。
    // 23:59 的每日提醒在 23:59:xx 武装、下一跳（默认 30 秒）落到 00:00:xx 才引爆；
    // 记成引爆当天的话，次日 23:59 会被「今天已弹过」挡掉——每日退化成隔天，且「错过必补」也救不回来
    //（那条检查排在补发之前）。任何把基准时刻推过午夜的延迟同理。
    [Fact]
    public void Daily_at_2359_still_fires_the_next_day_after_a_midnight_crossing()
    {
        var r = new Reminder { Trigger = "time", Time = "23:59", GraceMinutes = 5, Days = new() };
        var start = new DateTime(2026, 7, 15, 9, 0, 0);
        var st = new ReminderState();

        var armed = ReminderEngine.Decide(r, new DateTime(2026, 7, 15, 23, 59, 45), start, st);
        Assert.Equal("arm", armed.Action);
        st.PendingFireAt = armed.Base;                                   // App.ArmAt 的最小复刻（无延迟）

        Assert.Equal("fire", ReminderEngine.Decide(r, new DateTime(2026, 7, 16, 0, 0, 15), start, st).Action);
        Assert.Equal("2026-07-15", st.LastFiredDate);                    // 周三那次，不是周四

        Assert.Equal("arm", ReminderEngine.Decide(r, new DateTime(2026, 7, 16, 23, 59, 30), start, st).Action);
    }

    // 催促链睡过整个窗口后不该复活。23:50 触发、每 15 分钟催、催到 00:30，排到 00:05 时合盖休眠，
    // 次日 08:00 唤醒：截止早已过，这一跳不该引爆（以前不但引爆，续排时还会把截止重新解析成
    // 「明天 00:30」，于是接着催到 MaxRepeats——催了一上午）。
    [Fact]
    public void Stale_nag_chain_does_not_fire_after_sleeping_past_its_deadline()
    {
        var r = new Reminder { Trigger = "time", Time = "23:50", RepeatMinutes = 15, RepeatUntil = "00:30", Days = new() };
        var st = new ReminderState();
        ReminderEngine.UpdateAfterFire(r, new DateTime(2026, 7, 15, 23, 50, 0), "", st);   // 无人应答 → 排 00:05
        Assert.Equal(new DateTime(2026, 7, 16, 0, 5, 0), st.NextRepeatAt);

        var d = ReminderEngine.Decide(r, new DateTime(2026, 7, 16, 8, 0, 0), new DateTime(2026, 7, 15, 9, 0, 0), st);
        Assert.NotEqual("fire", d.Action);
        Assert.Null(st.NextRepeatAt);
        Assert.Equal(0, st.RepeatCount);
    }

    // 落盘的「稍后」只防过去、不防未来：系统时间前跳时点一次稍后（虚拟机还原 / 主板电池没电），
    // 时间校回来后这个未来时刻会把该提醒的所有分支永久挡死，界面无异常、编辑也没用（迁移原样带走），
    // 只能手删状态文件。超过一天的未来值一律当脏数据丢弃。
    [Fact]
    public void Absurdly_future_snooze_is_discarded_instead_of_silencing_forever()
    {
        var r = new Reminder { Trigger = "time", Time = "09:00", GraceMinutes = 5, Days = new() };
        var st = new ReminderState { SnoozeUntil = new DateTime(2030, 1, 1, 9, 0, 0) };
        var d = ReminderEngine.Decide(r, D(9, 2), D(8, 0), st);
        Assert.Null(st.SnoozeUntil);
        Assert.Equal("arm", d.Action);
    }

    [Fact]
    public void Tonight_snooze_crossing_midnight_is_still_honoured()   // 别把正常的跨午夜稍后一起丢了
    {
        var r = new Reminder { Trigger = "time", Time = "09:00", Days = new() };
        var soon = new DateTime(2026, 7, 16, 0, 20, 0);
        var st = new ReminderState { SnoozeUntil = soon };
        Assert.Equal("none", ReminderEngine.Decide(r, new DateTime(2026, 7, 15, 23, 55, 0), D(8, 0), st).Action);
        Assert.Equal(soon, st.SnoozeUntil);
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
