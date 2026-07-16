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
}
