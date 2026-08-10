using Clockwork.I18n;

namespace Clockwork.Core;

// 常用动作组模板（旧 PS 版 Get-ActionGroupTemplates 的移植）。每次调用现生成 → 各自新 id，重复添加不撞 id。
// 进程名用全球通用的（Slack / Discord / msedge），不按语言本地化——添加后按自己的软件改进程名即可。
// 步骤不设 label：列表摘要由 StepDisplay 按当前语言自动生成；组名/消息文本经 resx 本地化。
//
// 收录标准：常用、且演示至少一样别处没演示的能力。曾经膨胀到 10 个又砍回来（投屏演示与会议重叠 2/3、
// 夜间护眼的每一步别处都有、截图标注撞系统热键）——模板列表是新用户对「动作组能干什么」的第一印象，
// 七个各有一手比十个大同小异更有说服力。新模板先过这一关再进来。
public static class ActionGroupTemplates
{
    public static List<ActionGroup> All() => new()
    {
        // 专注：IM 用「最小化」而不是「关闭」——关掉会漏消息，现实中没人这么用。
        // 关通知排在最前：先把打断源掐掉，再收拾屏幕，顺序反了的话收拾到一半照样会被弹窗打断。
        // 它是有状态的开关（改注册表，不会自己恢复），所以「恢复常态」那个模板必须存在、且要配一条回来。
        new ActionGroup { Name = Strings.Get("Tpl_Focus"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "notificationsOff" },
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
        // 麦克风先静音：会前最容易出事的就是「以为没开麦」，这一步关的是系统默认录音设备本身，
        // 比任何会议软件自己的静音都彻底。进会后手动开麦即可——反过来（默认开着）风险大得多。
        new ActionGroup { Name = Strings.Get("Tpl_Meeting"), Steps = new()
        {
            new LaunchStep { Kind = "volume", Action = "micMute" },
            new LaunchStep { Kind = "system", Command = "notificationsOff" },
            new LaunchStep { Kind = "system", Command = "showDesktop" },
            new LaunchStep { Kind = "volume", Action = "set", Level = 70 },
        } },
        // 恢复常态：专注 / 会议关掉的东西，这里一次开回来。
        // 通知 / 麦克风静音都是有状态的开关——只给「关」不给「开」，用户第二天会以为通知坏了。
        // 亮度放最后且给 80 而不是 100：100 在多数笔记本上刺眼，80 是「回到正常工作亮度」
        // （模板里没有调暗的组，但亮度命令在，手动调暗过的人需要这个回程）。
        new ActionGroup { Name = Strings.Get("Tpl_Restore"), Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "notificationsOn" },
            new LaunchStep { Kind = "volume", Action = "micUnmute" },
            new LaunchStep { Kind = "volume", Action = "unmute" },
            new LaunchStep { Kind = "system", Command = "brightness", Level = 80 },
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
        // 唯一演示「整组循环」的模板：45 分钟一轮、跑 8 轮 ≈ 覆盖一个下午。
        new ActionGroup { Name = Strings.Get("Tpl_Sedentary"), Repeat = 8, RepeatDelayMs = 2700000, Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_SedentaryMsg") },
        } },
    };
}
