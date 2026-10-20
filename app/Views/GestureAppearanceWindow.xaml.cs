using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clockwork.Core;
using Clockwork.I18n;
// UseWindowsForms 的全局 using 让这几个类型撞名，显式钉到 WPF（同 Pickers.cs / StepMenu.cs 的惯例）。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Clockwork.Views;

// 笔迹外观：手势那条线画成什么样。
//
// 这一扇窗只管三件事——**样式**（画成什么）、**粗细**（画多粗）、**尾迹淡不淡出**——
// 外加一份**当场就能看见**的预览。
//
// 预览与屏幕上那条线**出自同一份画法**（GestureTrailRenderer）：预览一旦是另抄一份的，
// 它就会慢慢与真的那条线对不上，而预览的全部价值就是「所见即所得」——
// 抄一份的预览比没有预览更坏，它会骗人。
//
// 改动即时写进模型并存盘（同手势管理器）：这个窗口没有「确定 / 取消」，
// 挑完的那一刻意思就已经完整了——退不回去的东西写在界面上只会让人犹豫。
public partial class GestureAppearanceWindow : Window
{
    private readonly RootConfig _config;
    private readonly Action _save;
    // 两份预览：浅底与深底各一个渲染器。
    private readonly List<GestureTrailRenderer> _previews = new();
    private bool _loading;

    public GestureAppearanceWindow(RootConfig config, Action save)
    {
        _config = config;
        _save = save;
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _previews.Add(MakePreview(LightBox));
        _previews.Add(MakePreview(DarkBox));
        Populate();
        Refresh();
    }

    // 预览里的渲染器。画的是**一段弧**，不是折线：
    // 折线只看得见拐角，看不出圆角接头与笔触的节奏；
    // 而虚线与珠点这两种笔触的节奏，正是要在曲线上才看得出好不好看。
    private GestureTrailRenderer MakePreview(Border host)
    {
        var accent = (Brush)FindResource("BrushAccentText");
        // 命中色与屏幕上那条线同一个（近白）：预览要连「认出来了」那一下也让人看见。
        var armed = new SolidColorBrush(Color.FromRgb(0xE6, 0xEF, 0xFF));
        var renderer = new GestureTrailRenderer(
            GestureTrailStyles.Resolve(_config.Settings.GestureTrailStyle),
            GestureTrailWidths.Resolve(_config.Settings.GestureTrailWidth),
            accent, armed);
        host.Child = renderer.Surface;
        foreach (var p in Sample(host.Width, host.Height)) renderer.Add(p);
        return renderer;
    }

    // 预览里那一段弧：从左下起，抬起来，落到右下。
    // 起笔在左、收笔在右——**收笔那头正是亮头所在**，淡出的效果因此看得见。
    private static IEnumerable<Point> Sample(double w, double h)
    {
        const int N = 48;
        for (int i = 0; i <= N; i++)
        {
            double t = i / (double)N;
            double x = 14 + t * (w - 28);
            double y = h * 0.66 - Math.Sin(t * Math.PI) * h * 0.36;
            yield return new Point(x, y);
        }
    }

    // 样式名逐语言取。
    //
    // **这里逐条写死，不拼键名**：拼出来的键名（"Gesture_Style_" + id）既查不到
    // 「这一条到底翻没翻」，也过不了「文案表里有没有没人用的死键」那条检查。
    // 五条样式各写一行，是这个目录变长时唯一还能一眼看完的写法。
    private static string StyleName(string id) => id switch
    {
        "neon" => Strings.Get("Gesture_Style_Neon"),
        "rainbow" => Strings.Get("Gesture_Style_Rainbow"),
        "dashed" => Strings.Get("Gesture_Style_Dashed"),
        "dots" => Strings.Get("Gesture_Style_Dots"),
        _ => Strings.Get("Gesture_Style_Classic"),
    };

    private void Populate()
    {
        _loading = true;

        StyleCbo.Items.Clear();
        foreach (var style in GestureTrailStyles.All)
            StyleCbo.Items.Add(new ComboBoxItem { Content = StyleName(style.Id), Tag = style.Id });
        var currentStyle = _config.Settings.GestureTrailStyle;
        StyleCbo.SelectedItem = StyleCbo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => (string)x.Tag == currentStyle)
            ?? StyleCbo.Items[0];

        WidthCbo.Items.Clear();
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Off"), Tag = "off" });
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Thin"), Tag = "thin" });
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Normal"), Tag = "normal" });
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Thick"), Tag = "thick" });
        var currentWidth = _config.Settings.GestureTrailWidth;
        WidthCbo.SelectedItem = WidthCbo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => (string)x.Tag == currentWidth)
            ?? WidthCbo.Items[2];

        FadeChk.IsChecked = _config.Settings.GestureTrailFade;
        _loading = false;
    }

    // 把当前选择刷到预览上。
    //
    // **预览用的就是配置里的值**：这一扇窗没有草稿态，改一下就是改了。
    private void Refresh()
    {
        var style = GestureTrailStyles.Resolve(SelectedStyleId());
        var widths = GestureTrailWidths.Resolve(SelectedWidthId());
        bool fade = FadeChk.IsChecked == true;
        bool off = SelectedWidthId() == "off";
        foreach (var p in _previews)
        {
            p.SetStyle(style, widths, fade);
            p.SetArmed(false);
        }
        // 「不显示轨迹」那一档本来就没有线——预览里空着看着像坏了，所以另给一句话。
        OffNote.Visibility = off ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? SelectedStyleId() => (StyleCbo.SelectedItem as ComboBoxItem)?.Tag as string;

    private string? SelectedWidthId() => (WidthCbo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _config.Settings.GestureTrailStyle = GestureTrailStyles.Resolve(SelectedStyleId()).Id;
        _save();
        Refresh();
    }

    private void Width_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _config.Settings.GestureTrailWidth = SelectedWidthId() ?? "normal";
        _save();
        Refresh();
    }

    private void Fade_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.Settings.GestureTrailFade = FadeChk.IsChecked == true;
        _save();
        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
