namespace Clockwork.Core;

// 提醒的计时器运行时状态。按提醒 id 做键，跨 tick 保持。
public sealed class ReminderState
{
    public string LastFiredDate { get; set; } = "";
    // 用户手动「今天不再提醒」的日期（yyyy-MM-dd）。只与"今天"做等值比较，别的值一律惰性——
    // 故不需要 SnoozeUntil 那套陈旧值守卫（那是个会 gate 未来行为的时刻，写错能把提醒永久钉死；
    // 这里最坏是某个过期日期，明天自然失效）。落盘：跳过要扛得住重启，否则重启一次就白跳了。
    // 之所以另立字段而不复用 LastFiredDate：事件触发（解锁/空闲/插拔电源）的 ShouldFire 压根不看
    // LastFiredDate（事件一天可发生零次或十次），只有独立字段才能让三种触发共用一句「今天这条不响」。
    public string SkippedDate { get; set; } = "";
    public bool StartupHandled { get; set; }
    public DateTime? PendingFireAt { get; set; }
    // 这次已武装的触发是「为哪一天准备的」（yyyy-MM-dd）。与 PendingFireAt 同为会话态、不落盘。
    // 存在的理由：引爆时刻可能已经跨过午夜（23:59 的提醒 + 计时器间隔，或到点后延迟），
    // 若把 LastFiredDate 记成引爆当天，次日就会被「今天已弹过」挡掉——每日提醒退化成隔天一次。
    public string PendingForDate { get; set; } = "";
    public DateTime? NextRepeatAt { get; set; }
    // 本条催促链的绝对截止时刻，开链时解析一次就钉住。不能每次续排时按当时的 now 重新解析：
    // 「窗口是否跨午夜」的判断会在机器睡过头之后把截止一路往后推（23:50→00:30 的链，次日 08:00 唤醒
    // 会被重新解析成「明天 00:30」，于是接着催一整天）。会话态、不落盘。
    public DateTime? NextRepeatUntil { get; set; }
    // 一个字段两种含义，由提醒配没配「重复催促」决定，两条链互斥（配了催促就走不到自动稍后）：
    //   repeatMinutes>0 → 本条催促链的已催次数，上限 MaxRepeats。
    //   repeatMinutes<=0 → 连续无人应答的自动稍后轮数，上限 MaxAutoSnoozes（到顶降级成常驻卡片）。
    // 两种含义都在「人有明确表示」时清零：应答走 EndRepeatChain，手点稍后走 Snooze（仅后一种含义）。
    // 会话态、不落盘：重启后重新拿满额度，宁可多打扰一轮也不丢投递。
    public int RepeatCount { get; set; }
    public DateTime? SnoozeUntil { get; set; }
    // 循环运行的下一轮时刻。与 SnoozeUntil 同为耐久字段（落盘）——不落盘的话中午重启一次，
    // LastFiredDate 已是今天、Decide 不再武装，当天剩余轮次全部静默丢失。
    public DateTime? NextIntervalAt { get; set; }
}
