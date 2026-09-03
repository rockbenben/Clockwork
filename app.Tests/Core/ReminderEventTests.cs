using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 事件触发（空闲 / 锁屏 / 解锁 / 唤醒 / 插拔电源 / 低电量）的判定。
public class ReminderEventTests
{
    private static readonly DateTime Mon = new(2026, 7, 13, 12, 0, 0);   // 周一
    private static readonly DateTime Sat = new(2026, 7, 18, 12, 0, 0);   // 周六

    [Fact]
    public void Every_id_is_an_event_and_the_two_non_events_are_not()
    {
        // 逐个点名而不是只对个数：个数对得上但少了某一项、多了个拼错的 id，是同样的 12。
        // 每加一个事件都要来这里补一行——这正是想要的，它逼着人顺带确认另外三处
        //（resx 的 Ed_Trig_*、App 里的触发来源、编辑器里的参数行）也补齐了。
        Assert.Equal(
            new[] { "idle", "busy", "unlock", "lock", "resume", "acPlugged", "acUnplugged", "lowBattery",
                    "display", "netUp", "netDown", "usb" },
            ReminderEvent.All);
        Assert.All(ReminderEvent.All, id => Assert.True(ReminderEvent.IsEvent(id)));
        Assert.False(ReminderEvent.IsEvent("time"));
        Assert.False(ReminderEvent.IsEvent("startup"));
        Assert.False(ReminderEvent.IsEvent(null));
    }

    // 事件都不看周期（每 N 天 / 每月 / 仅一次）——新增的四个也一样，别让它们从这条规矩里漏出去。
    [Fact]
    public void No_event_uses_recurrence()
        => Assert.All(ReminderEvent.All, id => Assert.False(ReminderEvent.UsesRecurrence(id)));

    [Fact]
    public void ShouldFire_needs_enabled_and_a_matching_trigger()
    {
        var r = new Reminder { Trigger = "unlock", Enabled = true };
        Assert.True(ReminderEvent.ShouldFire(r, "unlock", Mon, null));
        Assert.False(ReminderEvent.ShouldFire(r, "lock", Mon, null));     // 别的事件不该把它带响
        r.Enabled = false;
        Assert.False(ReminderEvent.ShouldFire(r, "unlock", Mon, null));
    }

    // 星期过滤对事件同样有效——「工作日解锁时打卡」是这套东西最常见的用法。
    [Fact]
    public void ShouldFire_respects_the_weekday_filter()
    {
        var r = new Reminder { Trigger = "unlock", Days = new() { 1, 2, 3, 4, 5 } };
        Assert.True(ReminderEvent.ShouldFire(r, "unlock", Mon, null));
        Assert.False(ReminderEvent.ShouldFire(r, "unlock", Sat, null));
        Assert.True(ReminderEvent.ShouldFire(new Reminder { Trigger = "unlock" }, "unlock", Sat, null));   // 不限星期
    }

    [Fact]
    public void IdleDue_fires_once_per_absence()
    {
        var r = new Reminder { Trigger = "idle", IdleMinutes = 10 };
        Assert.False(ReminderEvent.IdleDue(r, 9, alreadyFired: false));
        Assert.True(ReminderEvent.IdleDue(r, 10, alreadyFired: false));
        Assert.True(ReminderEvent.IdleDue(r, 45, alreadyFired: false));
        // 已经响过这一轮：再空闲多久都不重复（复位由调用方在人回来时做）
        Assert.False(ReminderEvent.IdleDue(r, 45, alreadyFired: true));
    }

    [Fact]
    public void IdleMinutes_below_one_is_clamped_to_one()
        => Assert.True(ReminderEvent.IdleDue(new Reminder { IdleMinutes = 0 }, 1, false));

    // 连续使用是空闲的镜像，形状必须一模一样：够时长才响、一轮只响一次、下界夹到 1。
    [Fact]
    public void BusyDue_fires_once_per_streak()
    {
        var r = new Reminder { Trigger = "busy", BusyMinutes = 30 };
        Assert.False(ReminderEvent.BusyDue(r, 29, alreadyFired: false));
        Assert.True(ReminderEvent.BusyDue(r, 30, alreadyFired: false));
        Assert.True(ReminderEvent.BusyDue(r, 120, alreadyFired: false));
        // 这一轮已经提醒过：再坐多久也不重复催（复位由调用方在人离开满一分钟时做）
        Assert.False(ReminderEvent.BusyDue(r, 120, alreadyFired: true));
    }

    [Fact]
    public void BusyMinutes_below_one_is_clamped_to_one()
        => Assert.True(ReminderEvent.BusyDue(new Reminder { BusyMinutes = 0 }, 1, false));

    // idle 与 busy 读的是同一个空闲计数，所以同一时刻两者必须给出相反的答案——
    // 都为真意味着「人走了」和「人一直在」同时成立，那是这两个触发最容易写反的地方。
    [Fact]
    public void Idle_and_busy_never_both_fire_at_the_same_moment()
    {
        var idle = new Reminder { Trigger = "idle", IdleMinutes = 10 };
        var busy = new Reminder { Trigger = "busy", BusyMinutes = 30 };
        // 离开 10 分钟：空闲成立；此时 busy 的连续使用计数必然已被调用方清零（传 0）
        Assert.True(ReminderEvent.IdleDue(idle, 10, false));
        Assert.False(ReminderEvent.BusyDue(busy, 0, false));
        // 连续用了 30 分钟：空闲时长必然是 0
        Assert.True(ReminderEvent.BusyDue(busy, 30, false));
        Assert.False(ReminderEvent.IdleDue(idle, 0, false));
    }

    [Fact]
    public void LowBattery_only_on_battery_and_only_below_threshold()
    {
        var r = new Reminder { Trigger = "lowBattery", BatteryPercent = 20 };
        Assert.True(ReminderEvent.LowBatteryDue(r, percent: 20, onAc: false, alreadyFired: false));
        Assert.True(ReminderEvent.LowBatteryDue(r, percent: 5, onAc: false, alreadyFired: false));
        Assert.False(ReminderEvent.LowBatteryDue(r, percent: 21, onAc: false, alreadyFired: false));
        // 插着电就不是「电量偏低」该管的事
        Assert.False(ReminderEvent.LowBatteryDue(r, percent: 5, onAc: true, alreadyFired: false));
        Assert.False(ReminderEvent.LowBatteryDue(r, percent: 5, onAc: false, alreadyFired: true));
    }

    // 台式机读不到电量（percent<0）：永不触发，也永远算「已复位」——绝不在没有电池的机器上响。
    [Fact]
    public void LowBattery_never_fires_without_a_battery()
    {
        var r = new Reminder { BatteryPercent = 20 };
        Assert.False(ReminderEvent.LowBatteryDue(r, percent: -1, onAc: false, alreadyFired: false));
        Assert.True(ReminderEvent.LowBatteryReset(r, percent: -1, onAc: false));
    }

    [Fact]
    public void LowBattery_rearms_after_charging_or_plugging_in()
    {
        var r = new Reminder { BatteryPercent = 20 };
        Assert.False(ReminderEvent.LowBatteryReset(r, percent: 15, onAc: false));   // 还低着，别复位
        Assert.True(ReminderEvent.LowBatteryReset(r, percent: 25, onAc: false));    // 充回阈值以上
        Assert.True(ReminderEvent.LowBatteryReset(r, percent: 15, onAc: true));     // 插上电也算
    }

    // 空闲时长跨 49.7 天回绕：无符号相减照样得到正确差值，有符号会算出一个巨大的负数。
    [Fact]
    public void Idle_minutes_survive_tick_wraparound()
    {
        Assert.Equal(2, IdleTime.MinutesFrom(nowTick: 180_000, lastInputTick: 60_000));
        Assert.Equal(1, IdleTime.MinutesFrom(nowTick: 30_000, lastInputTick: uint.MaxValue - 29_999));
    }
}
