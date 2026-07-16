using Clockwork.I18n;

namespace Clockwork.Core;

// 常用动作组模板（旧 PS 版 Get-ActionGroupTemplates 的移植）。每次调用现生成 → 各自新 id，重复添加不撞 id。
// 用最普遍的默认进程名（微信 Weixin / QQ），面向大众常见习惯；添加后按自己的软件改进程名即可。
// 步骤不设 label：列表摘要由 StepDisplay 按当前语言自动生成；组名/消息文本经 resx 本地化。
public static class ActionGroupTemplates
{
    public static List<ActionGroup> All() => new()
    {
        new ActionGroup { Name = Strings.Get("Tpl_Focus"), Steps = new()
        {
            new LaunchStep { Kind = "window", Action = "close", Process = "Weixin" },
            new LaunchStep { Kind = "window", Action = "close", Process = "QQ" },
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "system", Command = "showDesktop" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_Meeting"), Steps = new()
        {
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "window", Action = "close", Process = "Weixin" },
            new LaunchStep { Kind = "window", Action = "close", Process = "QQ" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_EndOfDay"), Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_EndOfDayMsg"), Confirm = true },
            new LaunchStep { Kind = "window", Action = "close", Process = "Weixin" },
            new LaunchStep { Kind = "window", Action = "close", Process = "QQ" },
            new LaunchStep { Kind = "system", Command = "emptyRecycleBin" },
            new LaunchStep { Kind = "system", Command = "clearClipboard" },
            new LaunchStep { Kind = "system", Command = "lockScreen" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_Bedtime"), Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_BedtimeMsg"), Speak = true },
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "window", Action = "close", Process = "Weixin" },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_Away"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "lockScreen" },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_Screenshot"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "screenshot" },
            new LaunchStep { Kind = "app", Target = "mspaint.exe", DelayMs = 800 },
        } },
    };
}
