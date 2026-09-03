using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clockwork.Core;
using Clockwork.I18n;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这些名字两边都有。
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;

namespace Clockwork.Views;

// 图标选择器。返回值就是要存进配置的那串文本：码位（"E72E"）、图片路径，或空串（＝回到自动）。
//
// 点一下就选中并关窗，没有「确定」——一格一个结果，再要一次确认是白让人多点一下。
// 取消 / Esc 返回 null，调用方据此保持原值不动。
public partial class IconPickerWindow : Window
{
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    /// <summary>选中的图标。null ＝ 取消。</summary>
    public string? Picked { get; private set; }

    public IconPickerWindow(string current)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);

        int cur = IconLibrary.Parse(current);
        foreach (var cp in IconLibrary.Glyphs) Grid.Children.Add(MakeCell(cp, cp == cur));
    }

    private Button MakeCell(int codepoint, bool selected)
    {
        var b = new Button
        {
            // 字形必须挂在自建的 TextBlock 上、并清掉 ContentTemplate：全局 Button 样式的
            // ContentTemplate 里那个 TextBlock 会命中全局 TextBlock 样式的 FontFamily setter，
            // 而显式 setter 压过从按钮继承下来的值——只设 Button.FontFamily 的话字形取不到，
            // 渲染成一个空心方框（面板格子、页眉齿轮都踩过同一颗钉子）。
            Content = new TextBlock
            {
                Text = char.ConvertFromUtf32(codepoint),
                FontFamily = IconFont,
                FontSize = 18,
                Foreground = (Brush)FindResource(selected ? "BrushAccent" : "BrushPaper"),
            },
            ContentTemplate = null,
            Width = 32,
            Height = 32,
            Margin = new Thickness(1),
            MinWidth = 0,
            Padding = new Thickness(0),
            ToolTip = IconLibrary.Format(codepoint),
            BorderBrush = (Brush)FindResource(selected ? "BrushAccent" : "BrushLine"),
        };
        b.Click += (_, _) => { Picked = IconLibrary.Format(codepoint); DialogResult = true; };
        return b;
    }

    // 图标可以是图片，也可以是任意 exe（借它的图标），所以用通用文件选择器，不限定只能选图片。
    private void UseImage_Click(object sender, RoutedEventArgs e)
    {
        if (Pickers.BrowseFile(this) is not string p) return;   // 用户在文件对话框里取消了，这个窗留着
        Picked = p;
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Picked = "";
        DialogResult = true;
    }

    /// <summary>开一次选择器。返回新的图标值；null ＝ 取消，调用方不要动原值。</summary>
    public static string? Pick(Window? owner, string current)
    {
        var dlg = new IconPickerWindow(current) { Owner = owner };
        dlg.ShowDialog();
        return dlg.Picked;
    }
}
