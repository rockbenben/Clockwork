using System.Text.RegularExpressions;

namespace Clockwork.Core;

// message 步骤的三种呈现形态。Card 不拦路（右下角卡片），Info/Confirm 都是模态窗。
public enum MessageForm { Card, Info, Confirm }

// Core 小工具纯函数（重复次数夹取 / 步骤重复 / 时间阈值 / 插入位 / 省略号 / 进程名）。多处共用同一口径，避免魔数散落。
public static class StepHelpers
{
    // 重复次数夹取：<1→1，>999→999（防手写 json/输入框填出跑不完的序列）。
    public static int ClampRepeat(int n) => n < 1 ? 1 : (n > 999 ? 999 : n);

    // 步骤重复次数：夹到 1..999（C# 强类型，缺失即默认 1）。
    public static int StepRepeat(LaunchStep s) => ClampRepeat(s.Repeat);

    // 「仅 N 前」阈值的时/分（各自夹取）与「当天分钟数」。支持任意时刻（不再只整点）：时 0..23、分 0..59，
    // 越界回退 8:00（兼容旧配置只有 onlyBefore8 没有 beforeHour/beforeMinute——缺失即模型默认 8:00）。
    public static int BeforeHour(LaunchStep s) => (s.BeforeHour < 0 || s.BeforeHour > 23) ? 8 : s.BeforeHour;
    public static int BeforeMinute(LaunchStep s) => (s.BeforeMinute < 0 || s.BeforeMinute > 59) ? 0 : s.BeforeMinute;
    public static int BeforeMinutesOfDay(LaunchStep s) => BeforeHour(s) * 60 + BeforeMinute(s);
    public static string BeforeTimeLabel(LaunchStep s) => $"{BeforeHour(s):D2}:{BeforeMinute(s):D2}";

    // 「仅 N 后」阈值：与上面「仅 N 前」同一套夹取口径，越界回退 18:00（模型默认）。
    public static int AfterHour(LaunchStep s) => (s.AfterHour < 0 || s.AfterHour > 23) ? 18 : s.AfterHour;
    public static int AfterMinute(LaunchStep s) => (s.AfterMinute < 0 || s.AfterMinute > 59) ? 0 : s.AfterMinute;
    public static int AfterMinutesOfDay(LaunchStep s) => AfterHour(s) * 60 + AfterMinute(s);
    public static string AfterTimeLabel(LaunchStep s) => $"{AfterHour(s):D2}:{AfterMinute(s):D2}";

    // 开机延迟秒数夹取：0..600（10 分钟）。设置页与开机消费侧共用同一口径，避免魔数分家、UI 收了值而开机静默只等一半。
    public static int ClampStartupDelay(int seconds) => Math.Clamp(seconds, 0, 600);

    // 「插到第 index 项之后」的落点：index<0（无选中）或越界则追加到末尾。
    public static int InsertPosition(int index, int count) => (index >= 0 && index < count) ? index + 1 : count;

    // 文本超长截断加省略号（列表/标签显示用），默认 30 字。
    public static string Ellipsis(string text, int max = 30)
    {
        var t = text ?? "";
        if (t.Length <= max) return t;
        int cut = max;
        if (char.IsHighSurrogate(t[cut - 1])) cut--;   // 别切在代理对中间(emoji/扩展汉字)，否则末尾显示 �
        return t.Substring(0, cut) + "…";
    }

    // 归一进程标识：去目录（最后一个 / 或 \ 前全删）+ 去结尾 .exe（不分大小写），裸名原样。
    // 窗口动作/发送文本靠 GetProcessesByName 找窗口，它只认裸进程名。
    public static string ToProcessName(string value)
    {
        var n = Regex.Replace((value ?? "").Trim(), @".*[\\/]", "");
        return Regex.Replace(n, @"(?i)\.exe$", "");
    }

    // message 步骤呈现形态判定。三条路径（动作组运行 / 单步运行 / 开机清单）共用同一口径，别在各调用点重写。
    // Present="card" 优先：手改 json 同时配了 Confirm/OnYes 时卡片赢、后两者忽略——卡片只有「点击即关」
    // 一种交互（NotificationToast.xaml.cs:42），挂不了动作，让 Confirm 赢会造出一个永远点不到的是/否。
    // Present 为空或无法识别 → 沿用旧推导，盘上老配置行为逐字不变。
    public static MessageForm MessageFormOf(LaunchStep s)
        => s.Present == "card" ? MessageForm.Card
         : (s.Confirm || (s.OnYes != null && s.OnYes.Type != "none")) ? MessageForm.Confirm
         : MessageForm.Info;
}
