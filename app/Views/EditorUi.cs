using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Clockwork.Views;

// 步骤/提醒编辑器共用的小工具：显隐、组合框填充/取值、整数解析、星期勾选装载/收集。
// 两个编辑器此前各写一份逐字相同的 FillCombo/ComboVal/Vis/ParseOr 和 7 路 Day1..Day7 展开，统一到此。
internal static class EditorUi
{
    public static void Vis(UIElement el, bool visible) => el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public static string ComboVal(ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>填一个下拉，并挑中其中一项。</summary>
    /// <param name="selected">要挑中的值。表里没有它就算「挑不中」。</param>
    /// <param name="fallback">挑不中时改挑哪一项。**不给就落回第一项**（枚举类下拉的合理兜底）。
    /// 给了就按它挑——「挑不中时该落回哪一项」是**设计**，不是项的顺序该管的事。
    ///
    /// 为什么要这一格：Action 那一格是共用的，量纲随类型变（见 StepHelpers.DefaultActionFor）。
    /// 一条「关掉窗口」的步骤改成「音量」时，音量那个下拉里没有「关掉窗口」这一项，
    /// 落回第一项就成了「静音」——而「静音」是**第一项恰好排在前面**，不是谁定的。
    /// 同一类型在「新建」与「换类型」两条路上起手不一样，就是这么来的。</param>
    public static void FillCombo(ComboBox cb, (string Label, string Val)[] items, string selected, string? fallback = null)
    {
        cb.Items.Clear();
        int sel = 0;
        bool found = false;
        for (int i = 0; i < items.Length; i++)
        {
            cb.Items.Add(new ComboBoxItem { Content = items[i].Label, Tag = items[i].Val });
            if (items[i].Val == selected) { sel = i; found = true; }
        }
        if (!found && fallback != null)
        {
            for (int i = 0; i < items.Length; i++)
                if (items[i].Val == fallback) { sel = i; break; }
        }
        cb.SelectedIndex = items.Length > 0 ? sel : -1;
    }

    /// <summary>动作组下拉。与普通 FillCombo 的区别只有一条，但很要命：
    /// **目标组已被删除时不静默改指**。FillCombo 找不到 selected 会落回第一项，
    /// 于是一条指向已删除组的步骤/提醒，只要被打开看一眼再保存，就会悄悄改成指向别的组
    /// （或在提醒那边变成「无」）——界面上不会有任何提示，是无声的数据损坏。
    /// 这里给失联的目标单列一项、带上原 id：保存下来仍是原来那个 id，什么都不会被改掉，
    /// 而用户看得见「这个目标不见了」。
    /// 枚举类下拉不需要这一层——那种落回第一项是合理的规范化；这里落回的是**另一个对象**。</summary>
    /// <param name="withNone">是否提供「（无）」。提醒可以不绑组（有意义），
    /// 而 group 步骤不指向任何组只会在运行期报坏配置，故步骤编辑器不给这一项。</param>
    public static void FillGroupCombo(ComboBox cb, IReadOnlyList<ActionGroup> groups, string selected, bool withNone)
    {
        var items = new List<(string Label, string Val)>();
        if (withNone) items.Add((Strings.Get("Ed_Group_None"), ""));
        items.AddRange(groups.Select(g => (g.Name, g.Id)));
        if (!string.IsNullOrEmpty(selected) && !items.Any(i => i.Val == selected))
            items.Insert(withNone ? 1 : 0, (Strings.Get("Ed_GroupMissing"), selected));
        FillCombo(cb, items.ToArray(), selected);
    }

    /// <summary>启动清单条目下拉（面板语境的「引用启动项」）。与 FillGroupCombo 同一条命门：
    /// **目标条目已被删除时不静默改指**——单列一项露出原 id，保存下来仍指向原来那个。
    /// 不提供「（无）」：一条不指向任何条目的 step 步骤只会到运行期报坏配置（同 group 步骤的理由）。</summary>
    public static void FillRefCombo(ComboBox cb, IReadOnlyList<LaunchStep> items, string selected)
    {
        var list = new List<(string Label, string Val)>();
        list.AddRange(items.Select(s => (StepDisplay.StepListSummary(s), s.Id)));
        if (!string.IsNullOrEmpty(selected) && !list.Any(i => i.Val == selected))
            list.Insert(0, (Strings.Get("Ed_RefMissing"), selected));
        FillCombo(cb, list.ToArray(), selected);
    }

    // 整数解析：解析失败或越界回退 fallback。
    public static int ParseOr(string? s, int fallback, int min = int.MinValue, int max = int.MaxValue)
        => int.TryParse((s ?? "").Trim(), out var n) && n >= min && n <= max ? n : fallback;

    /// <summary>开一扇模态编辑窗。owner 为空时自己找位置、自己保证看得见。</summary>
    ///
    /// 为什么要有这一格：这两个入口都能从**面板**进来（面板一格右键 → 编辑；
    /// 主窗口收在托盘时 App 传的就是 null），而面板此刻已经自己关掉了。
    /// 不兜底的话，WindowStartupLocation=CenterOwner 没有 owner 可居中、Topmost 也没置，
    /// 于是模态开在任意位置、压在所有窗口后面 —— 用户看到面板消失、什么都没出现，
    /// 而一个模态框正拦着后续操作。
    ///
    /// 收在一处是因为这段推理从前被抄了两份（步骤编辑器、动作组编辑器），
    /// 而它是最容易被「顺手改掉」的那种：少一句 Topmost，症状是「面板一点就没了」。
    ///
    /// 品牌卡片没有并进来：它的判据是**多一条**的（owner 在但不可见时也要自己找位置，
    /// 且还要抢前台，见 BrandDialog.Make），不是同一份。
    public static bool? ShowModal(Window dlg, Window? owner)
    {
        if (owner == null) { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; dlg.Topmost = true; }
        return dlg.ShowDialog();
    }

    // 解析「仅 N 前 / 仅 N 后」阈值 "HH:mm"（时 0..23、分 0..59）；只填小时("8")也认，缺分作 0；
    // 非法整体回退 fallbackHour:00（「前」用 8、「后」用 18，与模型默认一致）。
    public static void ParseBeforeTime(string? text, out int hour, out int minute, int fallbackHour = 8)
    {
        hour = fallbackHour; minute = 0;
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var h) && h >= 0 && h <= 23) hour = h;
        if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var m) && m >= 0 && m <= 59) minute = m;
    }

    // 星期勾选。boxes 顺序即 周一..周日 (=1..7)。
    public static void LoadDays(IReadOnlyList<int>? days, params CheckBox[] boxes)
    {
        var d = days ?? new List<int>();
        for (int i = 0; i < boxes.Length; i++) boxes[i].IsChecked = d.Contains(i + 1);
    }

    public static List<int> CollectDays(params CheckBox[] boxes)
    {
        var days = new List<int>();
        for (int i = 0; i < boxes.Length; i++) if (boxes[i].IsChecked == true) days.Add(i + 1);
        return days;
    }
}
