using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Clockwork.Core;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这些名字两边都有（同别处那批别名）。
using Brush = System.Windows.Media.Brush;
using Grid = System.Windows.Controls.Grid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace Clockwork.Views;

// 把方向串画成它本来的样子：一条带起点圆点和末端箭头的折线。
//
// 为什么不用箭头文字（→↘↓）：**你认一个手势靠的是形状，不是读它的名字**。
// 一列 "→↘↓" 和 "↙←↖" 要一个字一个字读完才知道是哪条；画出来则一眼就分得开，
// 而且和你在画布上刚画的那一笔长得一模一样——列表里看到什么，手上就画什么。
// 这也是这个界面唯一花力气的地方，别处一律保持安静。
//
// 起点圆点是必要的，不是装饰：同一条折线倒着画是另一条手势（"RD" ≠ "DL"），
// 没有起点就分不出方向；末端箭头再确认一次朝向。
public static class GestureGlyph
{
    /// <summary>画一条手势。path 为空或非法时返回 null——调用方据此显示水印/占位，而不是画一个空盒子。</summary>
    public static FrameworkElement? Make(string? path, double w, double h, Brush stroke, double thickness = 2)
    {
        var pts = Trace(path);
        if (pts.Count < 2) return null;

        // 归一化到给定的框里，留出线宽和箭头的余量；单向手势（一条直线）某个轴的跨度为 0，
        // 此时按另一个轴缩放并居中，否则会除零或被拉成一条贴边的线。
        double pad = thickness * 2 + 4;
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
        }
        double spanX = maxX - minX, spanY = maxY - minY;
        double availW = w - pad * 2, availH = h - pad * 2;

        // **一格 = 固定像素，不是「各自撑满外接框」。**
        // 按外接框缩放时，扁的笔画（↙←↖，3 宽 1 高）撑满宽度、方的笔画（→↘↓，2×2）被高度限死，
        // 实测同一份清单里两条差 2.4 倍 —— 粗细不一，眼睛没法互相比较，读起来像随手涂的。
        // 共用单位之后，长的笔画就画得长、短的就短，像手写：这才是「同一套笔迹」。
        // 单位取「两格正好填满较短那一边」，于是最常见的 2×2 笔画刚好占满，更长的才按框收缩。
        double unit = System.Math.Min(availW, availH) / 2;
        double scale = System.Math.Min(unit,
                       System.Math.Min(spanX > 0 ? availW / spanX : double.MaxValue,
                                       spanY > 0 ? availH / spanY : double.MaxValue));
        if (double.IsInfinity(scale) || scale <= 0) scale = unit;
        double offX = (w - spanX * scale) / 2 - minX * scale;
        double offY = (h - spanY * scale) / 2 - minY * scale;
        var scr = pts.ConvertAll(p => new Point(p.X * scale + offX, p.Y * scale + offY));

        var fig = new PathFigure { StartPoint = scr[0], IsClosed = false };
        for (int i = 1; i < scr.Count; i++) fig.Segments.Add(new LineSegment(scr[i], true));
        var line = new Path
        {
            Data = new PathGeometry(new[] { fig }),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        var host = new Grid { Width = w, Height = h };
        host.Children.Add(line);
        host.Children.Add(Arrow(scr[^2], scr[^1], stroke, thickness));
        host.Children.Add(new Ellipse
        {
            Width = thickness * 2.2, Height = thickness * 2.2, Fill = stroke,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(scr[0].X - thickness * 1.1, scr[0].Y - thickness * 1.1, 0, 0),
        });
        return host;
    }

    /// <summary>方向串 → 单位格点折线。连续同向不合并——那是量化器的事，这里画的是最终结果。</summary>
    //
    // **原路折返要错开一点画。** ↑↓ 这种回头笔在单位格上是 (0,0)→(0,-1)→(0,0)：
    // 两条腿画在同一条线上，看起来就是一根竖线，和单独一条 ↓ 分不出来——
    // 而这个界面的立身之本正是「认一条手势靠形状，不是读它的名字」。
    // 遇到与前一笔恰好相反的方向，就给它以及后面所有点加一个横向偏移，
    // 折返那一笔便走在去程旁边，画出一个窄窄的 ∧ —— 而人画 ↑↓ 时手上本来就是这个形状。
    private const double RetraceSpread = 0.42;   // 单位格的几成宽。太小仍糊成一条，太大就不像「上去再回来」了

    // internal 而非 private：这段几何是「列表里两条手势看不看得出区别」的唯一出处，
    // 而经 Make() 去测要起 WPF 视觉树。留个缝给测试，比为测试改设计划算。
    internal static List<Point> Trace(string? path)
    {
        var pts = new List<Point>();
        if (string.IsNullOrEmpty(path)) return pts;
        double x = 0, y = 0, ox = 0, oy = 0;   // o = 累计的折返偏移
        int px = 0, py = 0;                    // 前一笔的单位位移
        pts.Add(new Point(0, 0));
        foreach (var raw in path)
        {
            // 位移查 GestureGate.Dirs——方向的定义只有那一处，画出来的和判出来的永远是同一套。
            if (GestureGate.Of(raw) is not { } d) return new List<Point>();
            // 与前一笔恰好相反：法线方向挪开一点，别让这一笔盖在上一笔身上。
            if (d.Dx == -px && d.Dy == -py)
            {
                double n = System.Math.Sqrt(d.Dx * d.Dx + d.Dy * d.Dy);
                ox += -d.Dy / n * RetraceSpread;
                oy += d.Dx / n * RetraceSpread;
            }
            x += d.Dx; y += d.Dy;
            px = d.Dx; py = d.Dy;
            pts.Add(new Point(x + ox, y + oy));
        }
        return pts;
    }

    // 末端箭头：从终点朝来向张开两条短边。长度跟着线宽走，缩略图和大图看起来才是同一支笔。
    private static Path Arrow(Point from, Point to, Brush stroke, double thickness)
    {
        double ang = System.Math.Atan2(to.Y - from.Y, to.X - from.X);
        double len = thickness * 3.2, spread = System.Math.PI / 7;
        Point Wing(double a) => new(to.X - len * System.Math.Cos(ang + a), to.Y - len * System.Math.Sin(ang + a));
        var fig = new PathFigure { StartPoint = Wing(spread), IsClosed = false };
        fig.Segments.Add(new LineSegment(to, true));
        fig.Segments.Add(new LineSegment(Wing(-spread), true));
        return new Path
        {
            Data = new PathGeometry(new[] { fig }),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }
}
