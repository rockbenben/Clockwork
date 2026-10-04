using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Clockwork.I18n;
using Xunit;

// **提示语指着界面上另一个控件说话时，它引用的必须是那个控件此刻的名字。**
//
// 这类"指向别处文案"的句子有一处必坏的属性：改那个标签的人不会想到还有一句话在引用它，
// 而 18 份文件里各改各的，谁也不会一起改。逐语言复核时它是最常见的一类真错——
// 复核定下的口径是：句子引用控件名，就引用该文件里那个控件的当前标签。
// 所以这条不判措辞，只判**包含**——标签换了字，句子必须跟着换。
//
// 引号形态各语言不同（德语 „…“、法语 « … »、中文「」），故不解析引号，
// 只要求标签的完整文本出现在句子里；法语在尖括号内侧留了不换行空格，两端空白一并剥掉。
public class HintQuotesItsOwnLabelTests
{
    private static readonly ResourceManager Rm =
        new("Clockwork.Resources.Strings", typeof(Strings).Assembly);

    private static Dictionary<string, string> Entries(string? code)
    {
        var set = Rm.GetResourceSet(
            code == null ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(code),
            createIfNotExists: true, tryParents: false);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set == null) return d;
        foreach (System.Collections.DictionaryEntry e in set)
            if (e.Key is string k && e.Value is string v) d[k] = v;
        return d;
    }

    // 句子引用了哪些标签：(被引的控件键, 引用它的那句键)。加新的指向型文案，往这里添一行。
    // 表里的每一行都是 18 份文件**当前全部成立**的指向：不成立的行不进表，
    // 因为一条半红的闸门只会被当成噪音，然后被人忽略掉整条。
    private static readonly (string Label, string Hint)[] Pairs =
    {
        ("System_ShowReadOnly", "System_Empty"),
        ("Ed_SendKey", "Warn_NotForCurrentWindow"),
        ("Kind_app", "Ed_IconHint"),
        ("Kind_mouse", "Ed_KeysHint"),
        ("Kind_copySelection", "Ed_UrlHint"),
        ("Theme_system", "Settings_ThemeTip"),
        ("Tab_Group", "Ed_NoGroupsHint"),
        ("Tab_Group", "Settings_UseGesturesTip"),
        ("Ed_ActSilent", "Val_SilentNoGroup"),
        ("Reminder_Popup_Yes", "Warn_OnYesGroupMissing"),
        ("Reminder_Popup_Yes", "Warn_OnYesGroupDisabled"),
        ("Autostart_Label", "Settings_StartupDelayTip"),
        ("Settings_PanelManage", "Settings_UsePanelTip"),
        ("Tab_Reminder", "Launch_Hint"),
        ("Tab_Launch", "Reminder_Hint"),
        ("Tab_Launch", "Group_Hint"),
        // 空态那句指着「新增」那颗按钮说话；按钮改名，这句话必须跟着改（18 份一起）。
        ("Btn_Add", "Group_NoSteps"),
    };

    public static IEnumerable<object[]> References =>
        Languages.All.SelectMany(l => Pairs.Select(p => new object[] { l.Code, p.Label, p.Hint }));

    [Theory]
    [MemberData(nameof(References))]
    public void The_hint_names_the_control_that_is_actually_on_screen(string code, string labelKey, string hintKey)
    {
        var values = code == "zh-CN" ? Entries(null) : Entries(code);
        // 按钮上的「…」是"后面还有对话框"的记号，句子里引用时没人带着它写，故剥掉再比。
        var label = values[labelKey].Replace('\u00a0', ' ').Trim().TrimEnd('…').TrimEnd();
        var hint = values[hintKey];
        Assert.True(hint.Contains(label, System.StringComparison.OrdinalIgnoreCase),
            // 忽略大小写：句子开头的标签必然被首字母大写改写（"Current window" → "The current window…"），
            // 那不算引用错了名字。屈折语（俄语把名字变格）仍是这条判据的盲区，宁可漏判不误判。
            $"{code}/{hintKey} 里引用的控件名与 {labelKey} 当前的文字不一致：\n" +
            $"  控件现在叫：{label}\n  那句写的是：{hint}");
    }
}
