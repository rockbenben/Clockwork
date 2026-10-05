using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Clockwork.Core;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这些名字两边都有。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;

namespace Clockwork.Views;

// 把一份图标规格画出来。管理器的格子、三个编辑器里的预览块，走的都是这一条路——
// 于是「编辑器里看到的那个图标」和「面板上真正画出来的那个」在结构上就是同一个，
// 不是靠两处代码各画一遍再指望它们一致。
public static class IconVisual
{
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    // 品牌图标的轮廓解析一次留一份：面板一次呼出要画二十几格，而 Geometry.Parse
    // 是逐字符解析路径串。冻结之后可以跨线程复用（缓存也是 static，面板会开很多次）。
    private static readonly ConcurrentDictionary<string, Geometry> Geometries = new();

    /// <summary>按规格画一个图标元素：能取到真实文件图标就用位图，取不到回退线描字形。</summary>
    /// <param name="size">画多大（像素）。字形按 0.78 折算，让它和位图在同一个方框里看着一样重。</param>
    public static FrameworkElement Make(PanelIconSpec spec, double size, Brush foreground)
    {
        // 装在一个壳里：位图是后台取的，取到之前先画字形，到货了再把壳里的内容换掉。
        // IconLoader 一秒都不能等（它跑在 UI 线程上，而那也是低级鼠标钩子的泵，见其注释），
        // 所以「先字形、后换图」不是为了好看，是唯一不会冻住程序的画法。
        var shell = new ContentControl { Focusable = false };
        void Paint()
        {
            var brand = Brand(spec, size, foreground);
            if (brand != null) { shell.Content = brand; return; }
            var bmp = spec.Kind == PanelIconKind.Image ? Native.IconLoader.Load(spec.Value, () => Repaint()) : null;
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                shell.Content = img;
                return;
            }
            shell.Content = Glyph(spec, size, foreground);
        }
        // 回调只在「壳还挂在界面上」时才重画：面板早就关掉了还去动它没有意义，
        // 而那时 shell 已经没人引用，重画只会把它自己留住。
        void Repaint() { if (PresentationSource.FromVisual(shell) != null) Paint(); }
        Paint();
        return shell;
    }

    private static uint ToHex(Color c) => ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static Geometry ParseFrozen(string mini)
    {
        var g = Geometry.Parse(mini);
        g.Freeze();
        return g;
    }

    /// <summary>品牌图标（`brand:slug`）画成一个矢量轮廓 + 官方品牌色；不是品牌规格返回 null，
    /// 由调用方走原来的位图/字形那条路。名字认不出来时同样返回 null —— 那一格回退线描字形，
    /// 而不是画一个空方框：配置是可以手改的，写错一个 slug 不该让面板缺一块。</summary>
    public static FrameworkElement? Brand(PanelIconSpec spec, double size, Brush foreground)
    {
        if (spec.Kind != PanelIconKind.Brand) return null;
        if (!BrandIcons.TryGet(BrandIcons.Prefix + spec.Value, out var icon)) return null;
        // 认不出前景色是什么（渐变/画刷没实色化）就照官方品牌色画：宁可少一次换色，
        // 也不要把一枚本来好好的 logo 换成猜出来的颜色。
        uint hex = foreground is SolidColorBrush ink ? BrandIcons.ColorFor(icon.Hex, ToHex(ink.Color)) : icon.Hex;
        var path = new Path
        {
            Data = Geometries.GetOrAdd(icon.Path, ParseFrozen),
            Fill = new SolidColorBrush(Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex)),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            // 显式限定：WinForms 那边也有一个叫 HorizontalAlignment 的枚举（这文件顶部就在治同类遮蔽）。
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        return path;
    }

    private static FrameworkElement Glyph(PanelIconSpec spec, double size, Brush foreground)
    {
        // 图片取不到就落回字形——包括「填了路径但那个文件已经没了」这种情形。
        // 不让格子空着是 PanelIconSpec.Fallback 定好的规矩，这里只是执行。
        return new TextBlock
        {
            Text = spec.Kind == PanelIconKind.Glyph ? spec.Value : spec.Fallback,
            FontFamily = IconFont,
            FontSize = size * 0.78,
            Foreground = foreground,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
    }

    /// <summary>把 <paramref name="host"/> 的内容换成这个图标。反复调用是安全的（改一次值刷一次预览）。</summary>
    public static void Fill(ContentControl host, PanelIconSpec spec, double size, Brush foreground)
        => host.Content = Make(spec, size, foreground);
}
