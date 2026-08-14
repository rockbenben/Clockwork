namespace Clockwork.Core;

// 提醒的计时器运行时状态。按提醒 id 做键，跨 tick 保持。
public sealed class ReminderState
{
    public string LastFiredDate { get; set; } = "";
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
    public int RepeatCount { get; set; }
    public DateTime? SnoozeUntil { get; set; }
    // 循环运行的下一轮时刻。与 SnoozeUntil 同为耐久字段（落盘）——不落盘的话中午重启一次，
    // LastFiredDate 已是今天、Decide 不再武装，当天剩余轮次全部静默丢失。
    public DateTime? NextIntervalAt { get; set; }
}
