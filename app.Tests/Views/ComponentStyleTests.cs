using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

// 组件层的跨窗一致性判据：尺寸、状态、浮层这三类东西只要各窗各写一套，
// 下一次改主题就一定会漂。这里钉住的是「一处定义、处处同高/同状态」的口径。
public class ComponentStyleTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string App(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root(), "app" }.Concat(parts).ToArray()));

    private static string NoComments(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);

    private static string StyleBlock(string theme, string key)
    {
        int at = theme.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"Theme.xaml 找不到样式 {key}");
        int end = theme.IndexOf("</Style>", at, StringComparison.Ordinal);
        Assert.True(end > at, $"Theme.xaml 的 {key} 没有闭合");
        return theme.Substring(at, end - at);
    }

    private static string ImplicitStyleBlock(string theme, string target)
    {
        int at = theme.IndexOf($"<Style TargetType=\"{target}\">", StringComparison.Ordinal);
        Assert.True(at >= 0, $"Theme.xaml 找不到隐式样式 {target}");
        int end = theme.IndexOf("</Style>", at, StringComparison.Ordinal);
        Assert.True(end > at, $"Theme.xaml 的 {target} 没有闭合");
        return theme.Substring(at, end - at);
    }

    [Fact]
    public void Single_line_inputs_use_the_same_28_dip_height()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "app"), "*.xaml", SearchOption.AllDirectories)
                                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var xaml = NoComments(File.ReadAllText(file));
            foreach (Match m in Regex.Matches(xaml, @"<(TextBox|ComboBox)\b[^>]*>", RegexOptions.Singleline))
            {
                var tag = m.Value;
                if (tag.Contains("AcceptsReturn=\"True\"", StringComparison.Ordinal)) continue;
                var h = Regex.Match(tag, @"\bHeight=""([^""]+)""");
                if (!h.Success) continue;
                Assert.True(h.Groups[1].Value == "28",
                    $"{Path.GetFileName(file)} 的单行输入不是 28 DIP：{Regex.Replace(tag, @"\s+", " ")}");
            }
        }
    }

    [Fact]
    public void Form_controls_get_their_height_from_the_theme()
    {
        var theme = App("Theme.xaml");
        Assert.Matches(@"Property=""MinHeight""\s+Value=""28""", ImplicitStyleBlock(theme, "TextBox"));
        Assert.Matches(@"Property=""MinHeight""\s+Value=""28""", ImplicitStyleBlock(theme, "ComboBox"));
    }

    [Fact]
    public void List_rows_share_one_height_and_center_their_content()
    {
        var theme = App("Theme.xaml");
        var item = ImplicitStyleBlock(theme, "ListBoxItem");
        Assert.Matches(@"Property=""MinHeight""\s+Value=""40""", item);
        Assert.Contains("ContentPresenter VerticalAlignment=\"Center\"", item);
        Assert.Matches(@"Property=""RowHeight""\s+Value=""40""", ImplicitStyleBlock(theme, "DataGrid"));

        var gesture = App("Views", "GestureManagerWindow.xaml.cs");
        Assert.Contains("Padding = new Thickness(10, 4, 10, 4)", gesture);
        Assert.Contains("MinHeight = 40", gesture);
    }

    [Fact]
    public void Navigation_tabs_show_a_keyboard_focus_ring()
    {
        var theme = App("Theme.xaml");
        foreach (var key in new[] { "PanelPageTab", "PanelTopTab" })
        {
            var block = StyleBlock(theme, key);
            Assert.Contains("x:Name=\"ring\"", block);
            Assert.Contains("IsKeyboardFocused", block);
        }
    }

    [Fact]
    public void Dropdown_items_distinguish_selection_from_hover()
    {
        var block = ImplicitStyleBlock(App("Theme.xaml"), "ComboBoxItem");
        Assert.Contains("IsSelected", block);
        Assert.Matches(@"IsHighlighted[\s\S]*?BrushSteel", block);
        Assert.Matches(@"IsSelected[\s\S]*?BrushAccentTint", block);
    }

    [Fact]
    public void Popup_surfaces_share_radius_and_menu_shadows()
    {
        var theme = App("Theme.xaml");
        Assert.Matches(@"Property=""HasDropShadow""\s+Value=""True""", ImplicitStyleBlock(theme, "ContextMenu"));
        Assert.Matches(@"Property=""HasDropShadow""\s+Value=""True""", ImplicitStyleBlock(theme, "ToolTip"));
        Assert.Contains("CornerRadius=\"8\"", ImplicitStyleBlock(theme, "ToolTip"));
        // WPF 的 Popup.HasDropShadow 是只读的：下拉弹层只求圆角同档，投影由菜单/提示层负责。
        Assert.Contains("CornerRadius=\"8\"", ImplicitStyleBlock(theme, "ComboBox"));
    }

    [Fact]
    public void Toast_uses_the_same_card_surface_as_the_other_overlays()
    {
        var toast = App("Views", "NotificationToast.xaml");
        Assert.Contains("CornerRadius=\"12\"", toast);
        Assert.Contains("BlurRadius=\"22\"", toast);
        Assert.Contains("ShadowDepth=\"5\"", toast);
    }

    [Fact]
    public void Radio_buttons_wrap_long_labels_and_text_lists_truncate_with_ellipsis()
    {
        var theme = App("Theme.xaml");
        var radio = ImplicitStyleBlock(theme, "RadioButton");
        Assert.Contains("TextWrapping=\"Wrap\"", radio);
        Assert.Contains("<ColumnDefinition Width=\"*\"", radio);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\"", radio);

        Assert.Contains("x:Key=\"ListTextEllipsis\"", theme);
        Assert.Contains("ItemTemplate=\"{StaticResource ListTextEllipsis}\"", App("Views", "BrandDialog.xaml"));
        Assert.Contains("ListTextEllipsis", App("Views", "Pickers.cs"));
    }
}
