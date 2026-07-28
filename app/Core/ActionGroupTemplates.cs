using Clockwork.I18n;

namespace Clockwork.Core;

// 常用动作组模板（旧 PS 版 Get-ActionGroupTemplates 的移植）。每次调用现生成 → 各自新 id，重复添加不撞 id。
// 进程名用全球通用的（Slack / Discord / msedge），不按语言本地化——添加后按自己的软件改进程名即可。
// 步骤不设 label：列表摘要由 StepDisplay 按当前语言自动生成；组名/消息文本经 resx 本地化。
public static class ActionGroupTemplates
{
    public static List<ActionGroup> All() => new()
    {
        // 专注：IM 用「最小化」而不是「关闭」——关掉会漏消息，现实中没人这么用。
        new ActionGroup { Name = Strings.Get("Tpl_Focus"), Steps = new()
        {
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "window", Action = "minimize", Process = "Slack" },
            new LaunchStep { Kind = "window", Action = "minimize", Process = "Discord" },
            new LaunchStep { Kind = "system", Command = "showDesktop" },
        } },
        // 会议：先清桌面（共享屏幕前别露隐私），收起 IM，最后把音量设到能听清的档位。
        // 绝不能用 volume/mute——那静的是整机输出，等于把自己的耳朵关掉。
        new ActionGroup { Name = Strings.Get("Tpl_Meeting"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "showDesktop" },
            new LaunchStep { Kind = "window", Action = "minimize", Process = "Slack" },
            new LaunchStep { Kind = "window", Action = "minimize", Process = "Discord" },
            new LaunchStep { Kind = "volume", Action = "set", Level = 70 },
        } },
        // 收工：确认闸门在最前（答「否」整组中止），锁屏必须在最后。
        // 不含「清空回收站」——不可逆动作不该塞进默认模板。
        new ActionGroup { Name = Strings.Get("Tpl_EndOfDay"), Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_EndOfDayMsg"), Confirm = true },
            new LaunchStep { Kind = "window", Action = "close", Process = "Slack" },
            new LaunchStep { Kind = "window", Action = "close", Process = "Discord" },
            new LaunchStep { Kind = "system", Command = "clearClipboard" },
            new LaunchStep { Kind = "system", Command = "lockScreen" },
        } },
        // 睡前：锁屏 + 息屏都要——只息屏的话动下鼠标就亮、机器还在跑。
        new ActionGroup { Name = Strings.Get("Tpl_Bedtime"), Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_BedtimeMsg"), Speak = true },
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "window", Action = "close", Process = "Slack" },
            new LaunchStep { Kind = "system", Command = "lockScreen" },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_Away"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "lockScreen" },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } },
        // 截图后剪贴板里是图，补一步 Ctrl+V 才闭环——否则画图开了还得用户自己粘贴。
        new ActionGroup { Name = Strings.Get("Tpl_Screenshot"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "screenshot" },
            new LaunchStep { Kind = "app", Target = "mspaint.exe", DelayMs = 800 },
            new LaunchStep { Kind = "keys", Combo = "Ctrl+V" },
        } },
        // 唯一演示「整组循环」的模板：45 分钟一轮、跑 8 轮 ≈ 覆盖一个下午。
        new ActionGroup { Name = Strings.Get("Tpl_Sedentary"), Repeat = 8, RepeatDelayMs = 2700000, Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_SedentaryMsg") },
        } },
    };
}
