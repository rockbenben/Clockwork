using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clockwork.Core;
using Clockwork.I18n;
// UseWindowsForms 的全局 using 让 Brush 与 System.Drawing.Brush 撞名，显式钉到 WPF（同 Pickers.cs 的惯例）。
using Brush = System.Windows.Media.Brush;

namespace Clockwork.Views;

// 「新增 ▾」步骤菜单：按用户意图分节（打开 / 控制程序 / 系统与声音 / 流程），
// 启动清单与动作组编辑器共用——分节结构在 StepDisplay.StepKindSections，只此一份。
public static class StepMenu
{
    // pick：选中某个步骤类型后的回调（收 kind id）。
    // firstOpenItem：插在「打开」节最前面的额外项——启动清单把「从开始菜单选择…」放这儿，
    // 它语义上就是「打开」的一种，且是零配置那条路，理应排最前。
    public static ContextMenu Build(Action<string> pick, MenuItem? firstOpenItem = null)
    {
        var menu = new ContextMenu();
        foreach (var (sectionKey, kinds) in StepDisplay.StepKindSections)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(SectionHeader(Strings.Get(sectionKey)));
            if (sectionKey == "Menu_SecOpen" && firstOpenItem != null) menu.Items.Add(firstOpenItem);
            foreach (var kind in kinds)
            {
                var k = kind;
                var mi = new MenuItem { Header = StepDisplay.StepKindLabel(k) };
                mi.Click += (_, _) => pick(k);
                menu.Items.Add(mi);
            }
        }
        return menu;
    }

    // 小节头是裸 TextBlock，刻意不用 MenuItem（第一版用了禁用的 MenuItem，被用户当成能点的菜单项——
    // 它继承了项目的行高与缩进，长得就像一行命令，只是点了没动静，正是「假控件」）。
    // ContextMenu.Items 可放任意元素：非 MenuItem 没有悬停高亮、没有项目边距，读作纯标签；
    // 键盘导航与读屏的菜单项遍历也都自动跳过它。
    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 10.5,
        Foreground = (Brush)System.Windows.Application.Current.Resources["BrushFaint"],
        Margin = new Thickness(10, 5, 0, 2),
    };
}
