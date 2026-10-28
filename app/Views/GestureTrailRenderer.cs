using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Clockwork.Core;
// UseWindowsForms 的全局 using 让这几个类型撞名，显式钉到 WPF（同 Pickers.cs / StepMenu.cs 的惯例）。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Clockwork.Views;

// 把一种笔迹样式画出来：**同一份画法，屏幕上那条线与管理器里的预览共用。**
//
// 两种画法：
//   · 流光 Ribbon：一条有宽窄变化的丝带——深钢带身 + 冰蓝亮芯 + 冷光外缘 + 深色描边；
//     起笔细、笔头展开、最后一小段收尖，像一笔带光的墨。
//   · 等粗线 Line：霓虹/彩虹/虚线/圆点的既有画法，层数由样式表决定。
//
// 为什么预览不另抄一份：预览一旦是抄的，它就会慢慢与真的那条线对不上，
// 而预览的全部价值就是「所见即所得」——抄一份的预览比没有预览更坏。
public sealed class GestureTrailRenderer
{
    /// <summary>亮头带多少个点。</summary>
    private const int HeadPoints = 16;

    /// <summary>淡出时（非亮头那部分）的透明度。</summary>
    private const double FadeOpacity = 0.45;

    // 描边那一层用的暗色：半透明的黑。
    private static readonly Brush HaloBrush = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));
    private static readonly Brush DefaultBody = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x20, 0x2E)));
    private static readonly Brush BrightHead = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xFA, 0xFF)));

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }

    // 样式带的 hex 色：解析一次、冻结后随渲染器复用。
    // 认不出来/空值返回 null，由调用方回落到主题色——配置文件可以手改，不能让一行坏色值炸掉笔迹。
    internal static Brush? BrushFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is not Color color) return null;
            return Frozen(new SolidColorBrush(color));
        }
        catch { return null; }
    }

    // 丝带的宽度轮廓：起笔细，到 85% 处最宽，最后 15% 收成尖。
    internal static double RibbonWidthFactor(double t)
    {
        if (t <= 0) return 0.22;
        if (t >= 1) return 0.08;
        if (t < 0.85)
        {
            double s = t / 0.85;
            return 0.22 + 0.78 * (s * s * (3 - 2 * s));
        }
        return 1 - 0.92 * ((t - 0.85) / 0.15);
    }

    // 把折线点放成左右两条边界，闭合成一条宽度随 t 变化的丝带。
    internal static Geometry BuildRibbon(IReadOnlyList<Point> points, double baseWidth, double extraWidth)
    {
        int n = points.Count;
        var left = new Point[n];
        var right = new Point[n];
        double nx = 0, ny = -1;
        for (int i = 0; i < n; i++)
        {
            var prev = points[Math.Max(0, i - 1)];
            var next = points[Math.Min(n - 1, i + 1)];
            double dx = next.X - prev.X, dy = next.Y - prev.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 0.0001) { nx = -dy / len; ny = dx / len; }
            double t = n <= 1 ? 1 : i / (double)(n - 1);
            double half = (baseWidth + extraWidth) * RibbonWidthFactor(t) / 2;
            left[i] = new Point(points[i].X + nx * half, points[i].Y + ny * half);
            right[i] = new Point(points[i].X - nx * half, points[i].Y - ny * half);
        }

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(left[0], isFilled: true, isClosed: true);
            for (int i = 1; i < n; i++) ctx.LineTo(left[i], isStroked: true, isSmoothJoin: false);
            for (int i = n - 1; i >= 0; i--) ctx.LineTo(right[i], isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    // 彩虹的两套画笔（没命中 / 命中）。
    // **做成实例字段而不是静态的**：WPF 的画笔归创建它的线程所有（线程亲和性），
    // 而静态字段只会被**第一次**用到它的那根线程创建——换一根线程去用就抛
    // 「另一个线程拥有该对象」。测试里每条用例各起一根 STA 线程，正会撞上这件事。
    private Brush? _rainbowIdle, _rainbowArmed;

    private readonly Brush _accent;
    private readonly Brush _armedBrush;
    private Brush _styleAccent;
    private Brush _styleCoreIdle;
    private Brush _styleBody;
    private readonly List<GestureTrailLayer> _layers = new();
    private readonly List<Polyline> _lines = new();
    private readonly List<Point> _points = new();
    private readonly Polyline _head;
    private Path? _ribbonGlow, _ribbonHalo, _ribbonBody;
    private Polyline? _ribbonCore;
    private Ellipse? _tip;

    private GestureTrailWidths _widths;
    private bool _armed;
    private bool _fade;
    private bool _rainbow;
    private bool _ribbon;
    private string _dash = "";

    /// <summary>承载这条线的画布。加进谁那儿，这根线就画在谁的坐标里。</summary>
    public Canvas Surface { get; }

    /// <summary>当前这一笔有没有点。</summary>
    public bool HasPoints => _points.Count > 0;

    /// <summary>当前这一笔的点数。</summary>
    /// <remarks>第二个点到位才真正显示——右键**点一下**（没画）只会有起点这一个点，
    /// 那时闪出一条线，等于把「右键菜单」变成「屏幕上闪一下再出菜单」。</remarks>
    public int PointCount => _points.Count;

    public GestureTrailRenderer(GestureTrailStyle style, GestureTrailWidths widths,
                                Brush accent, Brush armedBrush, bool fade = false)
    {
        _accent = accent;
        _styleAccent = accent;
        _styleCoreIdle = accent;
        _styleBody = DefaultBody;
        _armedBrush = armedBrush;
        _widths = widths;
        Surface = new Canvas { IsHitTestVisible = false };
        _head = MakeLine();
        Rebuild(style, fade);
    }

    /// <summary>换一种样式（或换粗细、换淡出）——**正在画的那一笔不会因此丢掉**。</summary>
    public void SetStyle(GestureTrailStyle style, GestureTrailWidths widths, bool fade)
    {
        _widths = widths;
        Rebuild(style, fade);
    }

    /// <summary>命中 / 没命中。颜色与宽度一起变。</summary>
    public void SetArmed(bool on)
    {
        if (_armed == on) return;
        _armed = on;
        ApplyLooks();
    }

    /// <summary>收笔：把这条线清空。</summary>
    public void Clear()
    {
        _points.Clear();
        _head.Points.Clear();
        if (_ribbon)
        {
            _ribbonCore?.Points.Clear();
            if (_tip != null) _tip.Visibility = Visibility.Collapsed;
            if (_ribbonGlow != null) _ribbonGlow.Data = null;
            if (_ribbonHalo != null) _ribbonHalo.Data = null;
            if (_ribbonBody != null) _ribbonBody.Data = null;
            return;
        }
        foreach (var l in _lines) l.Points.Clear();
    }

    /// <summary>线又到了一个点。</summary>
    public void Add(Point p)
    {
        _points.Add(p);
        if (_ribbon)
        {
            _ribbonCore?.Points.Add(p);
            _head.Points.Add(p);
            if (_head.Points.Count > HeadPoints) _head.Points.RemoveAt(0);
            UpdateTip();
            RebuildRibbonGeometry();
            return;
        }
        foreach (var l in _lines) l.Points.Add(p);
        // 亮头只留最近的一小段：多了就不是「笔尖」，而是半条线都亮着。
        _head.Points.Add(p);
        if (_head.Points.Count > HeadPoints) _head.Points.RemoveAt(0);
    }

    // 按样式重建这几根线，并把已有的点喂回去。
    private void Rebuild(GestureTrailStyle style, bool fade)
    {
        _layers.Clear();
        _layers.AddRange(style.Layers);
        _fade = fade;
        _rainbow = style.Rainbow;
        _ribbon = style.Ribbon;
        _dash = style.Dash;
        _styleAccent = BrushFromHex(style.Accent) ?? _accent;
        _styleCoreIdle = BrushFromHex(style.CoreIdle) ?? _accent;
        _styleBody = BrushFromHex(style.Body) ?? DefaultBody;

        Surface.Children.Clear();
        _lines.Clear();
        _head.Points.Clear();
        _ribbonGlow = _ribbonHalo = _ribbonBody = null;
        _ribbonCore = null;
        _tip = null;

        if (_ribbon)
        {
            _ribbonGlow = MakePath();
            _ribbonHalo = MakePath();
            _ribbonBody = MakePath();
            Surface.Children.Add(_ribbonGlow);
            Surface.Children.Add(_ribbonHalo);
            Surface.Children.Add(_ribbonBody);
            _ribbonCore = MakeLine();
            Surface.Children.Add(_ribbonCore);
            Surface.Children.Add(_head);
            _tip = new Ellipse { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Surface.Children.Add(_tip);
            ApplyLooks();
            foreach (var p in _points)
            {
                _ribbonCore.Points.Add(p);
                _head.Points.Add(p);
                if (_head.Points.Count > HeadPoints) _head.Points.RemoveAt(0);
            }
            UpdateTip();
            RebuildRibbonGeometry();
            return;
        }

        foreach (var layer in _layers)
        {
            var line = MakeLine();
            _lines.Add(line);
            Surface.Children.Add(line);
        }
        // 亮头压在最后：它是「笔尖」，该盖在线芯上面。
        Surface.Children.Add(_head);

        ApplyLooks();
        // 重建前的那些点重新喂一遍（换样式时这一笔可能正画到一半）。
        foreach (var p in _points)
        {
            foreach (var l in _lines) l.Points.Add(p);
            _head.Points.Add(p);
            if (_head.Points.Count > HeadPoints) _head.Points.RemoveAt(0);
        }
    }

    // 把「这一层的颜色、粗细、不透明度」按当前状态（命中 / 没命中、淡出 / 不淡出）刷一遍。
    private void ApplyLooks()
    {
        if (_ribbon) { ApplyRibbonLooks(); return; }

        double coreWidth = _armed ? _widths.CoreArmed : _widths.CoreIdle;
        for (int i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            var line = _lines[i];
            line.Stroke = BrushOf(layer);
            line.StrokeThickness = WidthOf(layer);
            // 虚线/珠点的图案按这一层的线宽换算过（见 DashOf），
            // 所以各层的虚线在**绝对长度**上是一致的——描边那一层比线芯宽一倍，
            // 不换算的话两层的虚线会错开，看着像两条线各画各的。
            line.StrokeDashArray = DashOf(_dash, line.StrokeThickness, coreWidth);
            // 淡出只压暗**线芯与辉光**：Halo 是浅底上的可读保险，LightHalo 是深底上的可读保险，
            // 把这两层也压暗，尾巴在其中一种底色上就等于没了——而那条尾巴正是要认形状的地方。
            line.Opacity = !_fade || layer.Role is GestureTrailRole.Halo or GestureTrailRole.LightHalo
                ? layer.Opacity
                : layer.Opacity * FadeOpacity;
        }
        // 亮头永远是全不透明的——它存在的意义就是「这里是笔尖」。
        _head.Stroke = CoreBrush();
        _head.StrokeThickness = coreWidth;
        _head.StrokeDashArray = DashOf(_dash, coreWidth, coreWidth);
        _head.Visibility = _fade ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyRibbonLooks()
    {
        double coreWidth = _armed ? _widths.CoreArmed : _widths.CoreIdle;
        if (_ribbonGlow != null)
        {
            _ribbonGlow.Fill = _styleAccent;
            _ribbonGlow.Opacity = _armed ? 0.27 : 0.18;
        }
        if (_ribbonHalo != null)
        {
            _ribbonHalo.Fill = HaloBrush;
            _ribbonHalo.Opacity = 1;
        }
        if (_ribbonBody != null)
        {
            _ribbonBody.Fill = _styleBody;
            _ribbonBody.Opacity = 0.62 * (_fade ? FadeOpacity : 1);
        }
        if (_ribbonCore != null)
        {
            _ribbonCore.Stroke = _armed ? _armedBrush : _styleCoreIdle;
            _ribbonCore.StrokeThickness = Math.Max(1.4, coreWidth * 0.55);
            _ribbonCore.Opacity = _fade ? FadeOpacity : 1;
        }
        // 亮头始终亮着（丝带的笔尖就是它的方向），命中时转纯白。
        _head.Stroke = _armed ? _armedBrush : BrightHead;
        _head.StrokeThickness = Math.Max(2.0, coreWidth * 0.8);
        _head.StrokeDashArray = null;
        _head.Opacity = 1;
        _head.Visibility = Visibility.Visible;
        if (_tip != null)
        {
            double size = Math.Max(3.0, coreWidth * 1.5);
            _tip.Width = size;
            _tip.Height = size;
            _tip.Fill = _head.Stroke;
            _tip.Visibility = _points.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        RebuildRibbonGeometry();
    }

    private void RebuildRibbonGeometry()
    {
        if (_ribbonGlow == null || _ribbonHalo == null || _ribbonBody == null) return;
        if (_points.Count < 2)
        {
            _ribbonGlow.Data = null;
            _ribbonHalo.Data = null;
            _ribbonBody.Data = null;
            return;
        }
        double baseBody = (_armed ? _widths.CoreArmed : _widths.CoreIdle) * 2.2;
        _ribbonGlow.Data = BuildRibbon(_points, baseBody, 5.5);
        _ribbonHalo.Data = BuildRibbon(_points, baseBody, 2.5);
        _ribbonBody.Data = BuildRibbon(_points, baseBody, 0);
    }

    private void UpdateTip()
    {
        if (_tip == null) return;
        if (_points.Count == 0) { _tip.Visibility = Visibility.Collapsed; return; }
        var p = _points[^1];
        double size = _tip.Width > 0 ? _tip.Width : 4;
        Canvas.SetLeft(_tip, p.X - size / 2);
        Canvas.SetTop(_tip, p.Y - size / 2);
        _tip.Visibility = Visibility.Visible;
    }

    private static Path MakePath() => new() { IsHitTestVisible = false };

    private Brush BrushOf(GestureTrailLayer layer) => layer.Role switch
    {
        GestureTrailRole.Halo => HaloBrush,
        GestureTrailRole.LightHalo => _styleAccent,
        GestureTrailRole.Glow => _styleAccent,
        _ => CoreBrush(),
    };

    // 线芯的画笔：命中时转成近白（**这就是「认出来了」那个信号**）。
    // 彩虹那一种命中时换成更亮的一套色相——整条线忽然变成纯白，
    // 会让人以为「换了个动作」，而它真正要说的是「这一下认出来了」。
    private Brush CoreBrush()
    {
        if (!_rainbow) return _armed ? _armedBrush : _styleCoreIdle;
        return _armed
            ? (_rainbowArmed ??= BuildRainbow(bright: true))
            : (_rainbowIdle ??= BuildRainbow(bright: false));
    }

    // 彩虹的渐变画笔：扫过一段冷色相（青 → 蓝 → 紫 → 玫），不再从红/黄起步。
    // 渐变是**相对包围盒**的（MappingMode 默认如此），所以它会跟着这一笔的长大自己铺开，
    // 不用每加一个点就重算一遍——这是它比「逐段上色」便宜得多的地方。
    private static Brush BuildRainbow(bool bright)
    {
        var stops = new GradientStopCollection();
        (double hue, double sat, double val)[] hues =
        {
            (190, 0.75, 1.0), (210, 0.80, 1.0), (230, 0.85, 1.0), (255, 0.80, 1.0),
            (275, 0.78, 1.0), (300, 0.75, 1.0), (325, 0.72, 1.0), (345, 0.70, 1.0),
        };
        for (int i = 0; i < hues.Length; i++)
        {
            var (h, s, v) = hues[i];
            // 命中时整体往白里提一档，而不是换成纯白。
            var c = HsvToColor(h, bright ? s * 0.75 : s, bright ? v * 0.55 + 0.45 : v);
            stops.Add(new GradientStop(c, i / (double)(hues.Length - 1)));
        }
        return new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 1));
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private double WidthOf(GestureTrailLayer layer)
    {
        double baseWidth = layer.Role == GestureTrailRole.Halo
            ? (_armed ? _widths.HaloArmed : _widths.HaloIdle)
            : (_armed ? _widths.CoreArmed : _widths.CoreIdle);
        return baseWidth * layer.WidthFactor;
    }

    private static Polyline MakeLine() => new()
    {
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    // 珠点那一档的笔触：一小段 + 一段空隙。
    //
    // **不用 DashStyles.Dot**（它是 [0, 3]，第一段长度为 0）：WPF 的零长度虚线不画东西，
    // 于是「珠点」那一档屏幕上只剩零星几点（实测：整条弧只剩两颗）。
    // 改成一小段（0.6 倍线宽）配上圆头笔帽——那正是「一颗珠子」该有的形状，
    // 而且长度不为零就一定会被画出来。
    private static readonly double[] DotBasis = { 0.6, 3 };

    // 笔触。Shape 上的虚线是 **StrokeDashArray**（不是 StrokeDashStyle），
    // 而它的单位是「线宽的倍数」——所以虚线与珠点天然跟着粗细走：粗一档，虚线也跟着粗一档。
    //
    // **按层换算**：描边比线芯宽（默认宽一倍），而倍数是相对线宽的，
    // 同一组倍数画在两层上，两层的**绝对**虚线长度就差着一倍（描边那层的虚线长一倍），
    // 于是两层对不齐——一条虚线档的笔迹会看成两条各画各的线。
    // 所以这里把倍数按「这一层多粗」折算，让各层的虚线在绝对长度上一致。
    private static DoubleCollection? DashOf(string dash, double layerWidth, double coreWidth)
    {
        if (dash.Length == 0) return null;
        var basis = dash == "dash"
            ? System.Windows.Media.DashStyles.Dash.Dashes.ToArray()
            : DotBasis;
        if (layerWidth <= 0 || coreWidth <= 0) return new DoubleCollection(basis);
        double k = coreWidth / layerWidth;
        var scaled = new DoubleCollection();
        foreach (var d in basis) scaled.Add(d * k);
        return scaled;
    }
}
