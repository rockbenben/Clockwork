using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;   // Visual3D：FindParent 里 `d is Visual or Visual3D` 用到
using Clockwork.Core;   // DropIndexCalc：落点算术摘成纯函数放这（单测覆盖）
// 项目开了 UseWindowsForms（托盘图标绘制要用），SDK 因此隐式 global using 了 System.Drawing /
// System.Windows.Forms——它们的 Point/Pen/Color/ButtonBase/DragDropEffects 和 WinForms 的
// DataGrid 与这里要用的 WPF 同名类型撞车，裸写会 CS0104 二义。用别名钉死到 WPF 版本，
// 其余代码不用跟着全限定名。
using DataGrid = System.Windows.Controls.DataGrid;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using DataGridRowHeader = System.Windows.Controls.Primitives.DataGridRowHeader;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Clockwork.Views;

// DataGrid 行拖拽重排。四张表共用：启动清单、定时任务、动作组列表，以及动作组编辑器里的步骤表——
// 前三者与最后一者的「顺序」含义不同（前者是执行/展示顺序，后者是步骤的执行顺序），但重排的手势一样。
// 一格一格点上/下移在十几步的组里是纯苦力。上/下按钮保留（键盘可达 + 精确微调）——
// 曾经把它们收进右键菜单，结果是把最高频的操作藏了起来，已经改回来。
//
// 只挂在已设 CanUserSortColumns=False 的表上：表头排序会让视图序与模型序脱钩，
// 拖拽算出来的落点就指不到正确的模型项。
internal static class DataGridReorder
{
    public static void Attach(DataGrid grid, Action<int, int> onMove)
    {
        // 打开行首那条带子当拖动手柄。挂在这里而不是各表的 XAML 里：只有走到这个方法的表才拖得动，
        // 手柄与能力因此永远同真同假——不会出现画了手柄却拖不动，也不会能拖却毫无提示。
        // 底部那行文字提示（「拖动行可调顺序」）远在列表之外，看不见；手柄就长在要抓的那一行上。
        // 拖拽识别照旧走 FindParent<DataGridRow>，行首带子在同一条视觉链上，无需另加分支。
        // 样式取到了才打开行首带子，顺序不能反：先开带子再找样式的话，样式一旦缺席（资源字典没并进来、
        // 键被改名），每行会多出一条系统默认的灰色空带子——既没手柄也没解释，而且这种降级完全无声。
        // 宁可没有手柄（拖拽照常能用，只是回到不够明显），也不要一条假装是手柄的空带子。
        if (grid.TryFindResource("DragGripRowHeader") is Style grip)
        {
            grid.RowHeaderStyle = grip;
            grid.HeadersVisibility = DataGridHeadersVisibility.All;
        }

        int from = -1;
        Point start = default;
        InsertionAdorner? adorner = null;

        void ClearAdorner()
        {
            if (adorner == null) return;
            AdornerLayer.GetAdornerLayer(grid)?.Remove(adorner);
            adorner = null;
        }

        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // 复选框/按钮上的按下不算起拖：那是「勾选启用」「点按钮」，抢走会让复选框点不动。
            // 但行首的拖动手柄必须放行——DataGridRowHeader 自己就继承自 ButtonBase（实测：
            // DataGridRowHeader -> ButtonBase -> ContentControl），不先认它，这道闸会把手柄整个挡掉：
            // 手柄画得出来、却从它拖不动，正是「有手柄 = 拖得动」这条承诺最难被发现的破法
            // （从别处拖照常能用，只有对着手柄拖才失灵，而手柄恰恰是在告诉人从这儿拖）。
            if (e.OriginalSource is DependencyObject d
                && FindParent<DataGridRowHeader>(d) == null
                && FindParent<ButtonBase>(d) != null) { from = -1; return; }
            var row = e.OriginalSource is DependencyObject o ? FindParent<DataGridRow>(o) : null;
            from = row?.GetIndex() ?? -1;
            start = e.GetPosition(grid);
        };

        grid.PreviewMouseMove += (_, e) =>
        {
            if (from < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(grid);
            // 过阈值才起拖：否则双击编辑（MouseDoubleClick）会被拖拽吃掉。
            if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(grid, from, DragDropEffects.Move);
            from = -1;
        };

        grid.DragOver += (_, e) =>
        {
            // 这些表都设了 AllowDrop=True，DragOver/Drop 因此会收到任何拖拽负载——包括从资源管理器拖来的
            // 文件。外来负载必须在这里主动拒绝（Effects=None），不能只是「什么都不做」：不拒绝就落到下面
            // 报 Move，Explorer 收到 DROPEFFECT_MOVE 会真的去移动/删除源文件，而这次 Drop 其实什么也没接住。
            if (!e.Data.GetDataPresent(typeof(int)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            int to = TargetIndex(grid, e.GetPosition(grid), out bool below);
            ClearAdorner();
            var target = RowAt(grid, to);
            if (target == null) return;
            var layer = AdornerLayer.GetAdornerLayer(grid);
            if (layer == null) return;
            adorner = new InsertionAdorner(target, below);
            layer.Add(adorner);
        };

        grid.DragLeave += (_, _) => ClearAdorner();

        grid.Drop += (_, e) =>
        {
            ClearAdorner();
            // 同上：非本控件负载必须报 None，不能悄悄吞掉——否则外来拖拽在 DragOver 就已经骗 Explorer
            // 说「我接住了、去移吧」，这里再默默 return 就是双重说谎（回执 Move，实际什么都没发生）。
            if (e.Data.GetData(typeof(int)) is not int src) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            int to = TargetIndex(grid, e.GetPosition(grid), out bool below);
            if (to < 0) return;
            to = DropIndexCalc.DropIndex(src, to, below, grid.Items.Count);
            if (src != to) onMove(src, to);
            e.Handled = true;
        };
    }

    // 命中的行号与「是否落在该行下半」。没命中任何行有两种截然不同的情形，必须分开判：
    //   · 落在列表下方空白（拖过最后一行）→ 末行、下半——拖到底。
    //   · 落在表头或首行上方空白（拖过第一行顶部/松早了）→ 首行、上半——拖到顶。
    // 原先两种都按第一种处理：往上越拖越远反而把行摔到最底，跟用户的手完全反着来。
    //
    // 定位必须用「已实例化的行」，不能固定看第 0 行的容器。三个 DataGrid 都开着行虚拟化（WPF 默认即开，
    // 项目里没有任何地方关掉），列表一滚动，第 0 行的容器就被回收、ContainerFromIndex(0) 返回 null——
    // 于是上面那条修复只对「短到不用滚动」的列表成立，长清单滚到中后部往上拖，照样摔到最底，还立刻存盘。
    // 返回 -1 = 判不出来（落在滚动条之类的地方），两个调用方都已按「小于 0 就别动」处理。
    private static int TargetIndex(DataGrid grid, Point p, out bool below)
    {
        below = false;
        var hit = grid.InputHitTest(p) as DependencyObject;
        var row = hit == null ? null : FindParent<DataGridRow>(hit);
        if (row == null)
        {
            DataGridRow? topRow = null, bottomRow = null;
            for (int i = 0; i < grid.Items.Count && topRow == null; i++) topRow = RowAt(grid, i);
            if (topRow != null && grid.TranslatePoint(p, topRow).Y < 0) return topRow.GetIndex();   // below 已是 false
            for (int i = grid.Items.Count - 1; i >= 0 && bottomRow == null; i--) bottomRow = RowAt(grid, i);
            if (bottomRow != null && grid.TranslatePoint(p, bottomRow).Y > bottomRow.ActualHeight) { below = true; return bottomRow.GetIndex(); }
            return -1;
        }
        var rp = grid.TranslatePoint(p, row);
        below = rp.Y > row.ActualHeight / 2;
        return row.GetIndex();
    }

    private static DataGridRow? RowAt(DataGrid grid, int index)
        => index < 0 || index >= grid.Items.Count
            ? null
            : grid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;

    // 命中点 → 它所属的行；表头/空白处返回 null。右键菜单那边也要问这个问题，
    // 统一走这里——另写一个走法就会漏掉 FindParent 里那道 Visual/逻辑树的分流
    // （非 Visual 的 DependencyObject 传给 VisualTreeHelper.GetParent 会抛）。
    internal static DataGridRow? RowFromHit(DependencyObject? hit)
        => hit == null ? null : FindParent<DataGridRow>(hit);

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}

// 插入位置指示线：贴在目标行的上缘或下缘。
internal sealed class InsertionAdorner : Adorner
{
    private readonly bool _below;
    private static readonly Pen Line = CreatePen();

    private static Pen CreatePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0x96, 0x3E)), 2);
        p.Freeze();
        return p;
    }

    public InsertionAdorner(UIElement target, bool below) : base(target)
    {
        _below = below;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = AdornedElement.RenderSize.Width;
        var y = _below ? AdornedElement.RenderSize.Height : 0;
        dc.DrawLine(Line, new Point(0, y), new Point(w, y));
    }
}
