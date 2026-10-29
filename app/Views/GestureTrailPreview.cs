using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clockwork.Core;
using Brush = System.Windows.Media.Brush;
using Grid = System.Windows.Controls.Grid;
using Point = System.Windows.Point;

namespace Clockwork.Views;

// 保存后的笔迹预览：屏幕上的渲染器 + 手势自己的起点/末端箭头。
// 管理器右侧、设置窗 swatch 都用它；列表缩略图仍只是形状索引，走 GestureGlyph。
public static class GestureTrailPreview
{
    /// <summary>把一条已保存的方向串画进 w×h 的预览框；路径为空/非法返回 null。</summary>
    public static FrameworkElement? MakePath(string? path, GestureTrailStyle style, GestureTrailWidths widths,
                                             bool fade, double w, double h, Brush fallbackAccent, Brush armed,
                                             double thickness = 2.4)
    {
        var points = GestureGlyph.Fit(path, w, h, thickness);
        if (points.Count < 2) return null;

        var renderer = new GestureTrailRenderer(style, widths, fallbackAccent, armed, fade);
        // 方向串只有拐点；丝带的宽窄变化需要沿线段也有采样点，否则一条单腿手势的预览
        // 只剩起笔/收笔两个宽度因子，看起来仍是一条细线。
        foreach (var p in Densify(points)) renderer.Add(p);

        var host = new Grid { Width = w, Height = h };
        host.Children.Add(renderer.Surface);
        var accent = GestureTrailRenderer.BrushFromHex(style.Accent) ?? fallbackAccent;
        if (GestureGlyph.Make(path, w, h, accent, thickness, includeLine: false) is { } ends)
            host.Children.Add(ends);
        return host;
    }

    /// <summary>样式选择器里的固定弧线样本，不带起点/箭头。</summary>
    public static FrameworkElement MakeArc(GestureTrailStyle style, GestureTrailWidths widths,
                                           double w, double h, Brush fallbackAccent, Brush armed)
    {
        var renderer = new GestureTrailRenderer(style, widths, fallbackAccent, armed);
        // 弧线形状只有 GestureTrailRenderer.SampleArc 一处（与设置页的预览共用）。
        foreach (var p in GestureTrailRenderer.SampleArc(w, h, 8, 0.74, 0.42)) renderer.Add(p);
        var host = new Grid { Width = w, Height = h };
        host.Children.Add(renderer.Surface);
        return host;
    }

    private static List<Point> Densify(IReadOnlyList<Point> points)
    {
        var result = new List<Point> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / 4));
            for (int j = 1; j <= steps; j++)
            {
                double t = j / (double)steps;
                result.Add(new Point(a.X + dx * t, a.Y + dy * t));
            }
        }
        return result;
    }
}
