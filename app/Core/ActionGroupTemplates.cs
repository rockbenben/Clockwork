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
        // 会议：先清桌面（共享屏幕前别露隐私），再把音量设到能听清的档位。
        // 绝不能用 volume/mute——那静的是整机输出，等于把自己的耳朵关掉。
        // 也不再逐个 window/minimize 收 IM：showDesktop 本就把所有窗口（含 Slack/Discord）一并最小化，
        // 而 window/minimize 的实现是「先还原并置前台、等 ~360ms、再最小化」——既是重复劳动，
        // 又会在刚清干净的（很可能正在共享的）屏幕上，把聊天窗口重新亮出来小一秒。
        new ActionGroup { Name = Strings.Get("Tpl_Meeting"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "showDesktop" },
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
            // lockScreen 后多留 1.5 秒再息屏：它只是 Process.Start 起 rundll32，进程一创建就返回，
            // 「已锁屏」远没完成，而锁屏过渡本身就是显示活动——紧接着 monitorOff 会黑一下又被唤醒。
            new LaunchStep { Kind = "system", Command = "lockScreen", DelayMs = 1500 },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } },
        new ActionGroup { Name = Strings.Get("Tpl_Away"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "lockScreen", DelayMs = 1500 },   // 同「睡前」：等锁屏落定再息屏
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } },
        // 截图：拉起框选浮层，再把画图备好，粘贴留给用户自己按 Ctrl+V。
        // 不再自动补这一步：screenshot 走的是 ms-screenclip: 交互式框选，图什么时候进剪贴板取决于用户
        // 拖完选区（数秒不等），任何固定延时都猜不准——早了按键被浮层吞掉，晚了就把剪贴板里的旧内容粘进画图，
        // 用户随后截的图反而叠在一坨无关内容上。
        new ActionGroup { Name = Strings.Get("Tpl_Screenshot"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "screenshot" },
            new LaunchStep { Kind = "app", Target = "mspaint.exe", DelayMs = 800 },
        } },
        // 唯一演示「整组循环」的模板：45 分钟一轮、跑 8 轮 ≈ 覆盖一个下午。
        new ActionGroup { Name = Strings.Get("Tpl_Sedentary"), Repeat = 8, RepeatDelayMs = 2700000, Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_SedentaryMsg") },
        } },
    };
}
