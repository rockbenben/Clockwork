using System.Linq;
using System.Text.RegularExpressions;
using Clockwork.I18n;

namespace Clockwork.Core;

// 步骤/系统命令/星期的显示文案。文案取自 resx（Strings.Get），随 UI 文化中/英切换。
public static class StepDisplay
{
    // 下拉顺序 = 这里的顺序：先「桌面上顺手做的事」，再显示器 / 通知 / 亮度这类环境开关，
    // 最后才是息屏 → 睡眠 → 休眠 → 注销 → 重启 → 关机这条越来越狠的下坡路（危险的排在末尾，不容易点错）。
    private static readonly string[] SysCmdIds =
    {
        "showDesktop", "lockScreen", "emptyRecycleBin", "openSettings", "screenshot", "clearClipboard",
        "setClipboard", "taskManager",
        "displayInternal", "displayClone", "displayExtend", "displayExternal",
        "notificationsOff", "notificationsOn", "brightness",
        "monitorOff", "sleep", "hibernate", "signOut", "restart", "shutdown",
    };

    // 带参数的系统命令：摘要里要把参数一起写出来（否则清单上三条「设置剪贴板文本」长得一模一样）。
    public static bool SystemCommandTakesText(string id) => id == "setClipboard";
    public static bool SystemCommandTakesLevel(string id) => id == "brightness";

    // 步骤类型 id 的规范顺序（步骤编辑器「类型」下拉用；标签一律经 StepKindLabel 本地化）。
    public static readonly string[] StepKinds =
        { "app", "keys", "text", "volume", "window", "system", "group", "delay", "message" };

    // 「新增 ▾」菜单的意图分节。九个机制名平铺时，新用户不知道「关掉微信」该点「窗口动作」还是
    // 「发送按键」——分节按「你想对什么做事」组织，机制名降到节内。节顺序即菜单顺序。
    // 必须恰好覆盖 StepKinds（有测试盯着）：漏一个那种步骤就没了入口，重一个会出现两条同名菜单项。
    public static readonly (string SectionKey, string[] Kinds)[] StepKindSections =
    {
        ("Menu_SecOpen", new[] { "app" }),
        ("Menu_SecControl", new[] { "window", "keys", "text" }),
        ("Menu_SecSystem", new[] { "volume", "system" }),
        ("Menu_SecFlow", new[] { "delay", "message", "group" }),
    };

    // 已知键则取译文，否则原样返回（未知 kind/command）。
    private static string OrRaw(string key, string raw)
    {
        var s = Strings.Get(key);
        return s == key ? raw : s;
    }

    public static string StepKindLabel(string kind) => OrRaw("Kind_" + kind, kind);

    // 有序系统命令表（编辑器下拉与摘要共用）：id 固定、标签本地化。
    public static IReadOnlyList<KeyValuePair<string, string>> SystemCommandMap()
        => SysCmdIds.Select(id => new KeyValuePair<string, string>(id, Strings.Get("Sys_" + id))).ToList();

    public static string SystemCommandLabel(string id) => OrRaw("Sys_" + id, id);

    // 星期集合 → 文案：空或全 7 天=每天，否则列出（中文连排「一二三」/英文空格分隔「Mon Tue」）。
    public static string DaysLabel(IEnumerable<int>? days)
    {
        var d = (days ?? Enumerable.Empty<int>()).ToList();
        if (d.Count == 0 || d.Count == 7) return Strings.Get("Days_EveryDay");
        var sep = Strings.Get("Days_Sep");
        return string.Join(sep, d.OrderBy(x => x).Where(x => x >= 1 && x <= 7).Select(x => Strings.Get("Day_" + x)));
    }

    private static string NoNewline(string s) => Regex.Replace(s ?? "", @"\r?\n", " ");

    private static string WinActionLabel(string action) => action switch
    {
        "close" => Strings.Get("Win_close"),
        "minimize" => Strings.Get("Win_minimize"),
        "maximize" => Strings.Get("Win_maximize"),
        "activate" => Strings.Get("Win_activate"),
        "sendkey" => Strings.Get("Win_sendkey"),
        _ => action,
    };

    public static string StepSummary(LaunchStep s)
    {
        string baseText = s.Kind switch
        {
            "app" => !string.IsNullOrEmpty(s.Label) ? s.Label : s.Target,
            "keys" => Strings.Lf("Sum_SendKeys", s.Combo),
            "volume" => s.Action switch
            {
                "mute" => Strings.Get("Vol_mute"), "unmute" => Strings.Get("Vol_unmute"), "set" => Strings.Lf("Vol_set", s.Level),
                "micMute" => Strings.Get("Vol_micMute"), "micUnmute" => Strings.Get("Vol_micUnmute"), _ => s.Action,
            },
            "window" => $"{WinActionLabel(s.Action)} {s.Process}",
            "system" => SystemCommandTakesText(s.Command)
                ? Strings.Lf("Sum_SysArg", SystemCommandLabel(s.Command), StepHelpers.Ellipsis(NoNewline(s.Text)))
                : SystemCommandTakesLevel(s.Command)
                    ? Strings.Lf("Sum_SysArg", SystemCommandLabel(s.Command), s.Level + "%")
                    : SystemCommandLabel(s.Command),
            "group" => Strings.Lf("Sum_RunGroup", !string.IsNullOrEmpty(s.Label) ? s.Label : (!string.IsNullOrEmpty(s.GroupId) ? s.GroupId : Strings.Get("Sum_Group_None"))),
            "delay" => s.DelayMs % 1000 == 0 ? Strings.Lf("Sum_Delay_Sec", s.DelayMs / 1000) : Strings.Lf("Sum_Delay_Ms", s.DelayMs),
            "message" => StepHelpers.MessageFormOf(s) == MessageForm.Card
                ? Strings.Lf("Sum_MsgCard", NoNewline(s.Message))
                : NoNewline(s.Message),
            "text" => Strings.Lf("Sum_Text", StepHelpers.Ellipsis(NoNewline(s.Text))),
            _ => s.Kind,
        };
        return baseText + DecorationSummary(s);
    }

    // 摘要的修饰段（×重复 + 各条件后缀），与主体分开取。步骤编辑器「条件与重复」折叠条的标题
    // 直接用它：同一份判定、同一份文案，编辑时看到的与列表里看到的严格一致，不会两处各解释一遍。
    public static string DecorationSummary(LaunchStep s)
    {
        var result = "";
        int rep = StepHelpers.StepRepeat(s);
        if (rep > 1) result += $" ×{rep}";
        var dc = (s.Days ?? new()).Where(x => x >= 1 && x <= 7).ToList();
        if (dc.Count > 0 && dc.Count < 7) result += Strings.Lf("Sum_DaysSuffix", DaysLabel(dc));
        if (s.OnlyBefore8) result += Strings.Lf("Sum_Before", StepHelpers.BeforeTimeLabel(s));
        if (s.OnlyAfter) result += Strings.Lf("Sum_After", StepHelpers.AfterTimeLabel(s));
        // 环境条件也要写进摘要：条件是「这一步为什么没跑」的唯一线索，藏在编辑器里等于没有。
        if (!string.IsNullOrWhiteSpace(s.IfProcess) && s.IfProcessMode is "running" or "notRunning")
            result += Strings.Lf(s.IfProcessMode == "running" ? "Sum_IfProcRunning" : "Sum_IfProcNot", StepHelpers.ToProcessName(s.IfProcess));
        if (s.IfPower == "ac") result += Strings.Get("Sum_IfAc");
        else if (s.IfPower == "battery") result += Strings.Get("Sum_IfBattery");
        if (!string.IsNullOrWhiteSpace(s.IfPathExists)) result += Strings.Lf("Sum_IfPath", StepHelpers.Ellipsis(s.IfPathExists.Trim(), 24));
        return result;
    }

    // 列表显示用摘要：用途说明作后缀。
    public static string StepListSummary(LaunchStep s)
    {
        var result = StepSummary(s);
        if (!string.IsNullOrEmpty(s.Note)) result += Strings.Lf("Sum_DaysSuffix", s.Note);
        return result;
    }
}
