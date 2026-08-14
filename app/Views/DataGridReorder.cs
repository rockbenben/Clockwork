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
using DragDropEffects = System.Windows.DragDropEffects;

namespace Clockwork.Views;

// DataGrid 行拖拽重排。启动清单与动作组步骤表共用——两处的「顺序即执行顺序」，
// 一格一格点上/下移在十几步的组里是纯苦力。上/下按钮保留（键盘可达 + 精确微调）。
//
// 只挂在已设 CanUserSortColumns=False 的表上：表头排序会让视图序与模型序脱钩，
// 拖拽算出来的落点就指不到正确的模型项。
internal static class DataGridReorder
{
    public static void Attach(DataGrid grid, Action<int, int> onMove)
    {
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
            if (e.OriginalSource is DependencyObject d && FindParent<ButtonBase>(d) != null) { from = -1; return; }
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
            // 两个表都设了 AllowDrop=True，DragOver/Drop 因此会收到任何拖拽负载——包括从资源管理器拖来的
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
