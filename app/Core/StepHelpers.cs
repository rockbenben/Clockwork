using System.Text.RegularExpressions;

namespace Clockwork.Core;

// message 步骤的三种呈现形态。Card 不拦路（右下角卡片），Info/Confirm 都是模态窗。
public enum MessageForm { Card, Info, Confirm }

// Core 小工具纯函数（重复次数夹取 / 步骤重复 / 时间阈值 / 插入位 / 省略号 / 进程名）。多处共用同一口径，避免魔数散落。
public static class StepHelpers
{
    /// <summary>「用户选择」的选项：一行一个，去掉空行与首尾空白。</summary>
    //
    // 一行一个而不是逗号分隔：选项里出现逗号是常态（「张三, 李四」是一个人名列表还是两个选项？），
    // 而换行永远不会有歧义。编辑器那一档给的正是一个多行框，形状与写法对得上。
    // 去重：两个一模一样的选项在列表里点哪个都一样，读起来像是坏了。
    public static List<string> ChoiceOptions(string? text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0 && seen.Add(t)) list.Add(t);
        }
        return list;
    }

    // 重复次数夹取：<1→1，>999→999（防手写 json/输入框填出跑不完的序列）。
    public static int ClampRepeat(int n) => n < 1 ? 1 : (n > 999 ? 999 : n);

    // 步骤重复次数：夹到 1..999（C# 强类型，缺失即默认 1）。
    // 消息步骤恒为 1：编辑器对它隐藏了重复行，所以那个值只可能是切换步骤类型时残留下来的
    //（「发送按键 ×3」改成「消息」→ repeat 仍是 3 → 每次运行连弹 3 张一模一样的卡片）。
    // 夹在这里而不是编辑器保存处，是因为本方法是所有读取方的唯一漏斗——启动清单、动作组、单步运行、
    // 列表摘要的「×N」后缀都走它，于是盘上已有的配置和手改的 json 立刻就对，不必等用户重新打开那一步保存一次。
    // 问句步骤（prompt / choice）同理恒为 1：同一个问题连问三遍没有意义，
    // 而答案写进同一个变量，每轮覆盖上一轮，只有最后一个能留下。
    // 曾经只夹了 message，于是编辑器给问句步骤露出了重复行、摘要跟着印一个「×N」，
    // 而 ActionGroupRunner 那个分支压根没有循环——界面承诺了一件引擎不做的事。
    public static int StepRepeat(LaunchStep s) => s.Kind is "message" or "prompt" or "choice" ? 1 : ClampRepeat(s.Repeat);

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
    // 「等剪贴板变化」的超时秒数。借用 Level 字段（0-100 的通用数值位）而不是新加一个：
    // 这个步骤只需要一个数，而模型里已经有一个没被它占用的数值位。
    // 夹到 1..60：0 秒等于不等（那就别放这一步），60 秒以上不像等剪贴板，像挂住了。
    public static int ClampWaitSeconds(int seconds) => seconds < 1 ? 5 : (seconds > 60 ? 60 : seconds);

    public static int ClampStartupDelay(int seconds) => Math.Clamp(seconds, 0, 600);

    // 中键长按阀值夹取：150..2000 ms。同样设置页与消费侧共用。
    //
    // 必须夹在**消费侧**而不只夹在设置页：设置页那一句只在用户重新保存时跑，
    // 而这个值还能从手改 / 导入的 json 直接进来（ConfigStore.Normalize 不夹任何数值）。
    // 下界 150 不是审美：LongPressGate 自己的底线只有 50 ms，比任何人的一次中键点击
    // （约 90 ms）都短——`"panelLongPressMs": 60` 这样的配置会让**每一次**中键点击都被
    // 判成长按：按下已经被吞、抬起也被吞，面板弹出来，于是新标签页 / 粘贴 / 自动滚动
    // 全系统失效，而用户看不出是这个程序干的。上界 2000：按到两秒还没反应，人早就以为坏了。
    public static int ClampLongPressMs(int ms) => Math.Clamp(ms, 150, 2000);

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
