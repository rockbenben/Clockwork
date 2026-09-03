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
    // 下拉按「什么在发生变化」分组排：人（空闲/连续使用）→ 会话（解锁/锁屏/唤醒）→ 电源 → 外设与网络。
    // 重排是安全的：FillCombo 按值选中而不是按下标，老配置切到新版本不会串项。
    //
    // 三类事件的来源各不相同，加新项前先想清楚归哪一类，否则会在错的地方找不到通知：
    //   · 订阅类（unlock/lock/resume/display）—— Windows 主动广播，WireSystemEvents 里订阅；
    //   · 消息类（usb）—— 广播到顶层窗口的 WM_，在 HotkeyHook 里接（本程序已有一个常驻窗口句柄）；
    //   · 轮询类（idle/busy/lowBattery/acPlugged/acUnplugged）—— 没有通知可订，搭提醒计时器的班车。
    public static readonly string[] All =
        { "idle", "busy", "unlock", "lock", "resume", "acPlugged", "acUnplugged", "lowBattery",
          "display", "netUp", "netDown", "usb" };

    public static bool IsEvent(string? trigger) => Array.IndexOf(All, trigger ?? "") >= 0;

    // 这个触发类型是否受「周期」（recurType / 每 N 天 / 每月某号 / 仅一次 / 起算日）约束。
    // 只有「按时间」受约束——事件和「登录时」都不看周期，编辑器为此把整块周期 UI 对它们隐藏。
    // 各处历史上是写成 !IsEvent(...) 的，那等于默认「非事件即时间型」，把 startup 漏在了受约束的一侧：
    // 编辑器隐藏了周期 UI 却原样保存旧值，运行期 Decide 又照着旧值过滤，于是把一条「每月 1 号」的提醒
    // 改成「登录时」之后，它只有每月 1 号登录才响；改自「仅一次」且日期已过的更是永远不再触发，
    // 而列表上一直写着「每次登录」。判据集中到这一个谓词，别再各处手写触发类型的补集。
    public static bool UsesRecurrence(string? trigger) => trigger == "time";

    // 本条提醒是否该响应这次 ev。星期过滤照旧生效（「工作日解锁时打卡」是真实需求）；
    // recurType 那一套（每 N 天 / 每月 / 仅一次）对事件没有意义，编辑器保存事件触发时会把它归成 daily。
    // st 只用来看「今天不再提醒」：这是本方法唯一读运行态的地方——事件的语义仍是"发生了就是发生了"，
    // 但用户手动跳过是凌驾于触发之上的表态，三种触发（时间/登录/事件）必须给出同一个答案，
    // 否则「今天不再」这句话在解锁类提醒上会变成一个安静的谎。刻意做成必填而不给默认值：
    // 可选参数会让下一个调用方漏传时静默丢掉这条保证，而现有测试全都不会发现。纯谓词测试显式传 null。
    public static bool ShouldFire(Reminder r, string ev, DateTime now, ReminderState? st)
    {
        if (r == null || !r.Enabled || r.Trigger != ev) return false;
        if (st != null && st.SkippedDate == ReminderEngine.DateKey(now)) return false;
        var days = r.Days ?? new();
        return days.Count == 0 || days.Contains(StepCondition.IsoDayOfWeek(now));
    }

    // 「空闲」是唯一需要轮询的事件（系统不发这个通知），故判定也在这儿：
    // 空闲时长够了且这一轮离开还没触发过 → 触发。fired 由调用方在人回来时复位，
    // 保证「一次离开只触发一次」——否则每个 tick 都满足条件，会一直响下去。
    public static bool IdleDue(Reminder r, int idleMinutes, bool alreadyFired)
        => !alreadyFired && idleMinutes >= (r.IdleMinutes < 1 ? 1 : r.IdleMinutes);

    // 「连续使用」是空闲的镜像：用满 BusyMinutes 分钟触发一次，人离开满一分钟后由调用方复位、重新计时。
    // 与 IdleDue 共用同一个「一轮只触发一次」的形状——两者读的是同一个 GetLastInputInfo，
    // 判据分家的话，「久坐提醒」和「离开提醒」会在同一次离席上给出互相矛盾的答案。
    //
    // 复位的粒度是一分钟，因为 IdleTime.Minutes() 就只有分钟精度（空闲事件一直用的也是它）。
    // ponytail: 离开多久算「歇过了」写死一分钟，没做成可配项——真有人嫌 55 秒的走神不该清零，
    // 再加一个 BusyResetMinutes 字段即可，判据已经收在这一个谓词里。
    public static bool BusyDue(Reminder r, int busyMinutes, bool alreadyFired)
        => !alreadyFired && busyMinutes >= (r.BusyMinutes < 1 ? 1 : r.BusyMinutes);

    // 「电量偏低」同理：跌破阈值触发一次，充回阈值以上才复位。percent<0 = 读不到电量（台式机）→ 永不触发。
    public static bool LowBatteryDue(Reminder r, int percent, bool onAc, bool alreadyFired)
        => !alreadyFired && !onAc && percent >= 0 && percent <= (r.BatteryPercent < 1 ? 1 : r.BatteryPercent);

    // 电量回到阈值以上（或插上电）即复位，允许下一次跌破时再响。
    public static bool LowBatteryReset(Reminder r, int percent, bool onAc)
        => onAc || percent < 0 || percent > (r.BatteryPercent < 1 ? 1 : r.BatteryPercent);
}
