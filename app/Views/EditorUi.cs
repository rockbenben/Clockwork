using System.Windows;
using System.Windows.Controls;
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

    // 整数解析：解析失败或越界回退 fallback。
    public static int ParseOr(string? s, int fallback, int min = int.MinValue, int max = int.MaxValue)
        => int.TryParse((s ?? "").Trim(), out var n) && n >= min && n <= max ? n : fallback;

    // 解析「仅 N 前」阈值 "HH:mm"（时 0..23、分 0..59）；只填小时("8")也认，缺分作 0；非法整体回退 08:00。
    public static void ParseBeforeTime(string? text, out int hour, out int minute)
    {
        hour = 8; minute = 0;
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
