namespace Clockwork.Core;

// 事件触发：不看钟表，看机器身上发生了什么。
//
// 有意不走 ReminderEngine.Decide 那条链：那套（周期日 / LastFiredDate / 宽限 / 错过必补 / 稍后）
// 整个是围绕「一天到某个点该响一次」建的，而事件一天可以发生零次也可以发生十次——
// 把「今天已弹过」套在解锁上，等于午休回来那次就没有了。所以 Decide 见到事件触发直接返回 none，
// 由 App 在事件发生的当下调 ShouldFire 挑出该响的条目。这里只剩两条规则：启用、且今天在星期范围内。
public static class ReminderEvent
{
    // 触发 id → 编辑器下拉与列表文案的 resx 键后缀（Ed_Trig_* / 见 ReminderDisplay）。顺序即下拉顺序。
    public static readonly string[] All =
        { "idle", "unlock", "lock", "resume", "acPlugged", "acUnplugged", "lowBattery" };

    public static bool IsEvent(string? trigger) => Array.IndexOf(All, trigger ?? "") >= 0;

    // 本条提醒是否该响应这次 ev。星期过滤照旧生效（「工作日解锁时打卡」是真实需求）；
    // recurType 那一套（每 N 天 / 每月 / 仅一次）对事件没有意义，编辑器保存事件触发时会把它归成 daily。
    public static bool ShouldFire(Reminder r, string ev, DateTime now)
    {
        if (r == null || !r.Enabled || r.Trigger != ev) return false;
        var days = r.Days ?? new();
        return days.Count == 0 || days.Contains(StepCondition.IsoDayOfWeek(now));
    }

    // 「空闲」是唯一需要轮询的事件（系统不发这个通知），故判定也在这儿：
    // 空闲时长够了且这一轮离开还没触发过 → 触发。fired 由调用方在人回来时复位，
    // 保证「一次离开只触发一次」——否则每个 tick 都满足条件，会一直响下去。
    public static bool IdleDue(Reminder r, int idleMinutes, bool alreadyFired)
        => !alreadyFired && idleMinutes >= (r.IdleMinutes < 1 ? 1 : r.IdleMinutes);

    // 「电量偏低」同理：跌破阈值触发一次，充回阈值以上才复位。percent<0 = 读不到电量（台式机）→ 永不触发。
    public static bool LowBatteryDue(Reminder r, int percent, bool onAc, bool alreadyFired)
        => !alreadyFired && !onAc && percent >= 0 && percent <= (r.BatteryPercent < 1 ? 1 : r.BatteryPercent);

    // 电量回到阈值以上（或插上电）即复位，允许下一次跌破时再响。
    public static bool LowBatteryReset(Reminder r, int percent, bool onAc)
        => onAc || percent < 0 || percent > (r.BatteryPercent < 1 ? 1 : r.BatteryPercent);
}
