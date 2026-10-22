using System.Globalization;
using Clockwork.I18n;

namespace Clockwork.Core;

// 出厂示例文本「跟着界面语言走」。
//
// 启动清单的示例、示例提醒、首启预置的那两个动作组——它们是**本地化的文案**，
// 却存在配置里（那是数据）。于是「换语言」只换界面、不换数据：
// 用户会看到一屏英文里夹着中文示例，而那些示例正是他用来搞懂这个程序的东西。
//
// 这不是本项目的首创顾虑——面板格子早就按同一条原则处理过了：
// 「不设 Label：标题由 StepDisplay 按当前语言现算……写死的话，
// 用户日后在设置里换一种语言，这些格子还留着装机时那一种。」
// 出厂内容必须是**当前语言**的那一份，不能是装机时冻结下来的那一份。
//
// 这里是同一原则在「用户可改的出厂内容」上的落地：
//   · **没被改过的**出厂文本 → 换成当前语言的那一份；
//   · **被改过的** → 一个字不动。
//
// 「改过没有」怎么判：存下来的字与**某一门语言**的出厂字全文相等 ⇒ 它就是出厂字
// （用户改过就会不一样）。认出是出厂字之后，换成当前语言的那一份。
// 全文相等这个判据是保守的：宁可漏翻，也不改用户自己写下的字。
public static class SampleText
{
    /// <summary>出厂文本涉及哪些文案键。</summary>
    //
    // Smp_*：启动清单的示例与示例提醒。
    // Tpl_Away / Tpl_EndOfDay / Tpl_EndOfDayMsg：首启预置的那两个动作组（名字与那句收工问话）。
    // Menu_SecCommon：首启那一页面板的名字。
    // 其余来自 StepDisplay.CommonPresets：示例手势的标题用的是同一批「常用操作」文案。
    internal static IEnumerable<string> BuiltInKeys()
    {
        yield return "Smp_Mute";
        yield return "Smp_OpenApp";
        yield return "Smp_OpenSite";
        yield return "Smp_OpenChat";
        yield return "Smp_ShowDesktop";
        yield return "Smp_RemWater";
        yield return "Smp_RemWrapUp";
        yield return "Smp_RemBills";
        yield return "Smp_RemSleep";
        yield return "Tpl_Away";
        yield return "Tpl_EndOfDay";
        yield return "Tpl_EndOfDayMsg";
        yield return "Menu_SecCommon";
        yield return "Cmd_toBottom";   // 示例手势里的那一条，CommonPresets 里没有
        foreach (var (labelKey, _, _) in StepDisplay.CommonPresets) yield return labelKey;
    }

    // 「任一语言的出厂文本 → 键」。与当前界面语言无关，所以建一次就够。
    private static Dictionary<string, string>? _index;

    private static Dictionary<string, string> Index()
    {
        if (_index != null) return _index;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, code) in Languages.All)
        {
            CultureInfo ci;
            try { ci = CultureInfo.GetCultureInfo(code); } catch { continue; }
            foreach (var key in BuiltInKeys())
            {
                string? text;
                try { text = Strings.TryGet(key, ci); } catch { continue; }
                // 两门语言给了同一个键同一段字（中文源与 zh-CN 就是同一份），后写的覆盖先写的，
                // 键是一样的，覆盖无害。两**个不同的键**撞成同一段字也不会出事：
                // 换出来的是其中一段出厂字，而出厂字之间本来就该是同一个意思。
                if (!string.IsNullOrEmpty(text)) map[text!] = key;
            }
        }
        _index = map;
        return map;
    }

    /// <summary>把配置里「还一模一样的出厂文本」换成当前语言的那一份。返回是否改动了什么。</summary>
    ///
    /// 必须在界面语言定下来之后调用（当前文化 = 界面语言），否则换出来的是旧语言的那一份。
    public static bool Relocalize(RootConfig cfg)
    {
        var index = Index();
        bool changed = false;

        // 下面每一张表都容一句空：反序列化能把集合置成 null（json 显式 null），
        // 而这里跑在启动路径上——为一个不存在的表炸掉整个启动不值得。
        foreach (var s in cfg.LaunchSteps ?? new List<LaunchStep>())
        {
            if (s == null) continue;
            if (Swap(s.Label, index, out var label)) { s.Label = label; changed = true; }
        }
        foreach (var r in cfg.Reminders ?? new List<Reminder>())
        {
            if (r == null) continue;
            if (Swap(r.Message, index, out var message)) { r.Message = message; changed = true; }
        }
        foreach (var g in cfg.ActionGroups ?? new List<ActionGroup>())
        {
            if (g == null) continue;
            if (Swap(g.Name, index, out var name)) { g.Name = name; changed = true; }
            foreach (var s in g.Steps ?? new List<LaunchStep>())
            {
                if (s == null) continue;
                if (Swap(s.Message, index, out var stepMessage)) { s.Message = stepMessage; changed = true; }
            }
        }
        // 手势与面板格子里存的是**组名的缓存副本**（组名一改，这些跟着走，见
        // StepDisplay.SyncGroupStepLabels）。组名刚被换过，这些缓存也得跟着换。
        foreach (var s in cfg.Gestures ?? new List<LaunchStep>())
        {
            if (s == null) continue;
            if (Swap(s.Label, index, out var gestureLabel)) { s.Label = gestureLabel; changed = true; }
        }
        foreach (var p in cfg.PanelPages ?? new List<PanelPage>())
        {
            if (p == null) continue;
            if (Swap(p.Name, index, out var pageName)) { p.Name = pageName; changed = true; }
            foreach (var s in p.Steps ?? new List<LaunchStep>())
            {
                if (s == null) continue;
                if (Swap(s.Label, index, out var tileLabel)) { s.Label = tileLabel; changed = true; }
            }
        }

        return changed;
    }

    // 认出出厂字就换成当前语言的那一份；认不出（用户自己写的、或者本来就是当前语言的）就不动。
    private static bool Swap(string? stored, Dictionary<string, string> index, out string next)
    {
        next = stored ?? "";
        if (string.IsNullOrEmpty(stored)) return false;
        if (!index.TryGetValue(stored, out var key)) return false;
        string? want;
        try { want = Strings.TryGet(key); } catch { return false; }
        // 这一门没有这条文案（翻译缺了）就别动 —— 换成键名本身是更糟的结果。
        if (string.IsNullOrEmpty(want) || want == stored) return false;
        next = want!;
        return true;
    }
}
