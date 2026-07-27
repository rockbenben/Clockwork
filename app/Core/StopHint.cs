namespace Clockwork.Core;

// 主窗口急停按钮是纯图形、一个字都没有，「这按钮是干嘛的」全压在悬停提示和屏幕阅读器名上（两处同一串）。
// 急停热键可在设置页改、也可清空（Delete 清空即不绑定），清空时不能留下一对空括号。
public static class StopHint
{
    // 括号用 ASCII 而非全角：这串在 18 种语言里共用一套拼法，全角括号只对中日文合适。
    public static string Compose(string label, string? hotkey)
    {
        hotkey = hotkey?.Trim();
        return string.IsNullOrEmpty(hotkey) ? label : $"{label} ({hotkey})";
    }
}
