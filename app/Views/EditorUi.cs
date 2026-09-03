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

    public static void FillCombo(ComboBox cb, (string Label, string Val)[] items, string selected)
    {
        cb.Items.Clear();
        int sel = 0;
        for (int i = 0; i < items.Length; i++)
        {
            cb.Items.Add(new ComboBoxItem { Content = items[i].Label, Tag = items[i].Val });
            if (items[i].Val == selected) sel = i;
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

    // 整数解析：解析失败或越界回退 fallback。
    public static int ParseOr(string? s, int fallback, int min = int.MinValue, int max = int.MaxValue)
        => int.TryParse((s ?? "").Trim(), out var n) && n >= min && n <= max ? n : fallback;

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
