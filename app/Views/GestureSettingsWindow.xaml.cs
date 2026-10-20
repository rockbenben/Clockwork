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

// 手势设置：全局的那几项——灵敏度、笔迹的样子、尾迹淡不淡、动作名显不显示、
// 哪些程序里根本不接管手势。
//
// **它们从前散在管理器页脚的几个控件里**，而那一扇窗的高度预算已经用满
// （内容 390、兜底上限 392）：再往里加，被挤到窗外去的是「关闭」那一行。
// 搬出来之后，管理器只回答一件事——「有哪些手势」；这里回答另一件事——「它们整体怎么表现」。
//
// 预览与屏幕上那条线**出自同一份画法**（GestureTrailRenderer）：预览一旦是另抄一份的，
// 它就会慢慢与真的那条线对不上，而预览的全部价值就是「所见即所得」——
// 抄一份的预览比没有预览更坏，它会骗人。
public partial class GestureSettingsWindow : Window
{
    private readonly RootConfig _config;
    private readonly Action _save;
    // 两份预览：浅底与深底各一个渲染器。
    private readonly List<GestureTrailRenderer> _previews = new();
    private bool _loading;

    public GestureSettingsWindow(RootConfig config, Action save)
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
            double x = 16 + t * (w - 32);
            double y = h * 0.66 - Math.Sin(t * Math.PI) * h * 0.36;
            yield return new Point(x, y);
        }
    }

    private void Populate()
    {
        _loading = true;

        SensitivityCbo.Items.Clear();
        SensitivityCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Sensitivity_High"), Tag = "high" });
        SensitivityCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Sensitivity_Normal"), Tag = "normal" });
        SensitivityCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Sensitivity_Low"), Tag = "low" });
        var currentSens = _config.Settings.GestureSensitivity;
        SensitivityCbo.SelectedItem = SensitivityCbo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => (string)x.Tag == currentSens) ?? SensitivityCbo.Items[1];

        StyleCbo.Items.Clear();
        foreach (var style in GestureTrailStyles.All)
            StyleCbo.Items.Add(new ComboBoxItem { Content = StyleName(style.Id), Tag = style.Id });
        var currentStyle = _config.Settings.GestureTrailStyle;
        StyleCbo.SelectedItem = StyleCbo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => (string)x.Tag == currentStyle) ?? StyleCbo.Items[0];

        WidthCbo.Items.Clear();
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Off"), Tag = "off" });
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Thin"), Tag = "thin" });
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Normal"), Tag = "normal" });
        WidthCbo.Items.Add(new ComboBoxItem { Content = Strings.Get("Gesture_Trail_Thick"), Tag = "thick" });
        var currentWidth = _config.Settings.GestureTrailWidth;
        WidthCbo.SelectedItem = WidthCbo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => (string)x.Tag == currentWidth) ?? WidthCbo.Items[2];

        FadeChk.IsChecked = _config.Settings.GestureTrailFade;
        ShowActionLabelChk.IsChecked = _config.Settings.GestureShowActionLabel;

        AddIgnoreBtn.Content = Strings.Get("Gesture_IgnoreAdd");
        PaintIgnoreApps();

        _loading = false;
    }

    // 样式名逐语言取。
    //
    // **这里逐条写死，不把键名拼出来**：拼出来的那种（拿前缀接上 id）既查不到
    // 「这一条到底翻没翻」，也过不了「文案表里有没有没人用的死键」那条检查——
    // 那个检查认的是字面量，五条样式各写一行，是这个目录变长时唯一还能一眼看完的写法。
    private static string StyleName(string id) => id switch
    {
        "neon" => Strings.Get("Gesture_Style_Neon"),
        "rainbow" => Strings.Get("Gesture_Style_Rainbow"),
        "dashed" => Strings.Get("Gesture_Style_Dashed"),
        "dots" => Strings.Get("Gesture_Style_Dots"),
        _ => Strings.Get("Gesture_Style_Classic"),
    };

    // 名单：写出来给人看（空的时候说「还没加」）。
    //
    // 按钮上带当前份数，名单里的名字也进提示——空与不空差别大到界面上必须看得出来。
    private void PaintIgnoreApps()
    {
        var list = _config.Settings.GestureIgnoreApps;
        IgnoreList.Text = list.Count > 0 ? string.Join(", ", list) : Strings.Get("Gesture_IgnoreEmpty");
        EditIgnoreBtn.Content = list.Count > 0
            ? Strings.Lf("Gesture_IgnoreAppsN", list.Count)
            : Strings.Get("Gesture_IgnoreApps");
        EditIgnoreBtn.ToolTip = list.Count > 0
            ? string.Join(", ", list)
            : Strings.Get("Gesture_IgnoreHint");
    }

    // 把当前选择刷到预览上。
    //
    // **预览用的就是配置里的值**：这一扇窗没有草稿态，改一下就是改了。
    private void Refresh()
    {
        var style = GestureTrailStyles.Resolve(SelectedStyleId());
        var widths = GestureTrailWidths.Resolve(SelectedWidthId());
        bool fade = FadeChk.IsChecked == true;
        foreach (var p in _previews)
        {
            p.SetStyle(style, widths, fade);
            p.SetArmed(false);
        }
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

    private void Sensitivity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SensitivityCbo.SelectedItem is ComboBoxItem item && item.Tag is string sens)
        {
            _config.Settings.GestureSensitivity = sens;
            _save();
        }
    }

    private void Fade_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.Settings.GestureTrailFade = FadeChk.IsChecked == true;
        _save();
        Refresh();
    }

    private void ShowActionLabel_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.Settings.GestureShowActionLabel = ShowActionLabelChk.IsChecked == true;
        _save();
    }

    // 手打那份名单：一张多行文本卡，一行一个程序名。
    //
    // 名单通常只有一两条，而逐个从「选程序」对话框里挑要开好几轮——所以这条是主路。
    private void EditIgnore_Click(object sender, RoutedEventArgs e)
    {
        var current = string.Join("\n", _config.Settings.GestureIgnoreApps);
        var edited = BrandDialog.AskMulti(this, Strings.Get("Gesture_IgnoreTitle"),
                                          Strings.Get("Gesture_IgnoreMessage"), current);
        if (edited == null) return;   // 取消：名单一个字都没动
        _config.Settings.GestureIgnoreApps = GestureGate.ParseIgnoreList(edited);
        _save();
        PaintIgnoreApps();
    }

    // 「加一个程序…」：走现成的程序选择器，省掉「这个进程叫什么」那道坎
    // （分应用绑定那边本来就用它）。
    //
    // 名单的归一在 Core.GestureGate.ParseIgnoreList（纯函数、有测试）：
    // 去掉首尾空白与路径、去掉 .exe 后缀（与分应用绑定同一套写法）、去掉重复项、去掉空行。
    private void AddIgnore_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.PickProcess(this) is not string picked) return;
        var name = StepHelpers.ToProcessName(picked);
        if (name.Length == 0) return;
        var list = _config.Settings.GestureIgnoreApps;
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(name);
            _config.Settings.GestureIgnoreApps = list;
            _save();
        }
        PaintIgnoreApps();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
