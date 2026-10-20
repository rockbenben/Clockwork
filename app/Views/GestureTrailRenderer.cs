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
// 为什么不把画法留在 GestureTrailWindow 里、预览另抄一份：预览一旦是抄的，
// 它就会慢慢与真的那条线对不上（颜色差一点、虚线的间隔差一点），
// 而预览的全部价值就是「所见即所得」——抄一份的预览比没有预览更坏，它会骗人。
// 于是画法抽在这里，两处都拿它画。
//
// 三件事在这里决定：
//   ① **层次**：描边（垫底的那层暗的）/ 辉光（罩上去的亮的那层）/ 线芯（真正看见的那条线）。
//   ② **笔触**：实线 / 虚线 / 珠点——同一层线换一种画法，不换形状。
//   ③ **淡出**：尾迹变暗、笔头留一段亮的（彗星那样）。这一条是唯一会动到「画了什么」的，
//      所以它做成一个**开关**而不是一种样式——要淡出的人不想因此换掉线的画法。
//
// 亮头是**额外的一根线**，不是把线芯调亮：WPF 的线只有一根笔，
// 想让「尾巴暗、笔头亮」就只能再画一根短一些的、盖在上面。
// 点的个数（HeadPoints）是算过的：一笔大概几十到几百个采样点，
// 亮头留十几个点刚好是「笔尖后面那一小段」，再多就变成半条线都亮。
public sealed class GestureTrailRenderer
{
    /// <summary>亮头带多少个点。</summary>
    private const int HeadPoints = 16;

    /// <summary>淡出时（非亮头那部分）的透明度。</summary>
    private const double FadeOpacity = 0.45;

    // 描边那一层用的暗色：半透明的黑。
    // 不用主题色不是要好看，是要在**任何**底色上都读得出来（白文档、深色 IDE、花壁纸）。
    private static readonly Brush HaloBrush = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));

    // 彩虹的两套画笔（没命中 / 命中）。
    // **做成实例字段而不是静态的**：WPF 的画笔归创建它的线程所有（线程亲和性），
    // 而静态字段只会被**第一次**用到它的那根线程创建——换一根线程去用就抛
    // 「另一个线程拥有该对象」。测试里每条用例各起一根 STA 线程，正会撞上这件事。
    // 每根线自己建两份画笔的代价可以忽略（八个色标，建一次）。
    private Brush? _rainbowIdle, _rainbowArmed;

    private readonly Brush _accent;
    private readonly Brush _armedBrush;
    private readonly List<GestureTrailLayer> _layers = new();
    private readonly List<Polyline> _lines = new();
    private readonly List<Point> _points = new();
    private readonly Polyline _head;

    private GestureTrailWidths _widths;
    private bool _armed;
    private bool _fade;
    private bool _rainbow;
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
        _armedBrush = armedBrush;
        _widths = widths;
        Surface = new Canvas { IsHitTestVisible = false };
        _head = MakeLine();
        Rebuild(style, fade);
    }

    /// <summary>换一种样式（或换粗细、换淡出）——**正在画的那一笔不会因此丢掉**。</summary>
    // 换样式是用户当场就能做的事，而这一笔可能正画到一半：
    // 重建折线时把已有的点重新喂回去，那一笔就接着画。
    public void SetStyle(GestureTrailStyle style, GestureTrailWidths widths, bool fade)
    {
        _widths = widths;
        Rebuild(style, fade);
    }

    /// <summary>命中 / 没命中。线的颜色与粗细一起变。</summary>
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
        foreach (var l in _lines) l.Points.Clear();
        _head.Points.Clear();
    }

    /// <summary>线又到了一个点。</summary>
    public void Add(Point p)
    {
        _points.Add(p);
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
        _dash = style.Dash;

        Surface.Children.Clear();
        _lines.Clear();
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
            // 淡出只压暗**线芯与辉光**：描边那一层是「在任何底色上都看得见」的最后一道保险，
            // 把它也压暗，浅色底上的尾巴就等于没了——而那条尾巴正是要认形状的地方。
            line.Opacity = !_fade || layer.Role == GestureTrailRole.Halo
                ? layer.Opacity
                : layer.Opacity * FadeOpacity;
        }
        // 亮头永远是全不透明的——它存在的意义就是「这里是笔尖」。
        _head.Stroke = CoreBrush();
        _head.StrokeThickness = coreWidth;
        _head.StrokeDashArray = DashOf(_dash, coreWidth, coreWidth);
        _head.Visibility = _fade ? Visibility.Visible : Visibility.Collapsed;
    }

    private Brush BrushOf(GestureTrailLayer layer) => layer.Role switch
    {
        GestureTrailRole.Halo => HaloBrush,
        GestureTrailRole.Glow => _armed ? _armedBrush : _accent,
        _ => CoreBrush(),
    };

    // 线芯的画笔：命中时转成近白（**这就是「认出来了」那个信号**）。
    // 彩虹那一种命中时换成更亮的一套色相——整条线忽然变成纯白，
    // 会让人以为「换了个动作」，而它真正要说的是「这一下认出来了」。
    private Brush CoreBrush()
    {
        if (!_rainbow) return _armed ? _armedBrush : _accent;
        return _armed
            ? (_rainbowArmed ??= BuildRainbow(bright: true))
            : (_rainbowIdle ??= BuildRainbow(bright: false));
    }

    // 彩虹的渐变画笔：色相绕一圈。
    // 渐变是**相对包围盒**的（MappingMode 默认如此），所以它会跟着这一笔的长大自己铺开，
    // 不用每加一个点就重算一遍——这是它比「逐段上色」便宜得多的地方。
    private static Brush BuildRainbow(bool bright)
    {
        var stops = new GradientStopCollection();
        (double hue, double sat, double val)[] hues =
        {
            (0, 0.85, 1.0), (30, 0.85, 1.0), (60, 0.85, 1.0), (140, 0.75, 1.0),
            (185, 0.80, 1.0), (220, 0.85, 1.0), (285, 0.80, 1.0), (360, 0.85, 1.0),
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
