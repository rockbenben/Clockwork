using System.Linq;
using System.Text.RegularExpressions;
using Clockwork.I18n;

namespace Clockwork.Core;

// 步骤/系统命令/星期的显示文案。文案取自 resx（Strings.Get），随 UI 文化中/英切换。
public static class StepDisplay
{
    private static readonly string[] SysCmdIds =
    {
        "showDesktop", "lockScreen", "emptyRecycleBin", "openSettings", "screenshot", "clearClipboard",
        "taskManager", "monitorOff", "sleep", "hibernate", "signOut", "restart", "shutdown",
    };

    // 步骤类型 id 的规范顺序（启动清单「新增▾」菜单 + 步骤编辑器共用；标签一律经 StepKindLabel 本地化）。
    public static readonly string[] StepKinds =
        { "app", "keys", "text", "volume", "window", "system", "group", "delay", "message" };

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
            "volume" => s.Action switch { "mute" => Strings.Get("Vol_mute"), "unmute" => Strings.Get("Vol_unmute"), "set" => Strings.Lf("Vol_set", s.Level), _ => s.Action },
            "window" => $"{WinActionLabel(s.Action)} {s.Process}",
            "system" => SystemCommandLabel(s.Command),
            "group" => Strings.Lf("Sum_RunGroup", !string.IsNullOrEmpty(s.Label) ? s.Label : (!string.IsNullOrEmpty(s.GroupId) ? s.GroupId : Strings.Get("Sum_Group_None"))),
            "delay" => s.DelayMs % 1000 == 0 ? Strings.Lf("Sum_Delay_Sec", s.DelayMs / 1000) : Strings.Lf("Sum_Delay_Ms", s.DelayMs),
            "message" => NoNewline(s.Message),
            "text" => Strings.Lf("Sum_Text", StepHelpers.Ellipsis(NoNewline(s.Text))),
            _ => s.Kind,
        };
        var result = baseText;
        int rep = StepHelpers.StepRepeat(s);
        if (rep > 1) result += $" ×{rep}";
        var dc = (s.Days ?? new()).Where(x => x >= 1 && x <= 7).ToList();
        if (dc.Count > 0 && dc.Count < 7) result += Strings.Lf("Sum_DaysSuffix", DaysLabel(dc));
        if (s.OnlyBefore8) result += Strings.Lf("Sum_Before", StepHelpers.BeforeTimeLabel(s));
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
