namespace Clockwork.Core;

// 提醒的计时器运行时状态。按提醒 id 做键，跨 tick 保持。
public sealed class ReminderState
{
    public string LastFiredDate { get; set; } = "";
    public bool StartupHandled { get; set; }
    public DateTime? PendingFireAt { get; set; }
    public DateTime? NextRepeatAt { get; set; }
    public int RepeatCount { get; set; }
    public DateTime? SnoozeUntil { get; set; }
    // 循环运行的下一轮时刻。与 SnoozeUntil 同为耐久字段（落盘）——不落盘的话中午重启一次，
    // LastFiredDate 已是今天、Decide 不再武装，当天剩余轮次全部静默丢失。
    public DateTime? NextIntervalAt { get; set; }
}
