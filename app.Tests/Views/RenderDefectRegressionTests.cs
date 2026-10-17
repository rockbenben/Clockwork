using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

// **钉住四个「图里看得见、代码里读不出」的渲染缺陷。**
//
// 它们同属一类：**结构上完全逃得过测试，逃过 code review，逃过读码表**，只有把窗口渲染出来
// 用眼睛看才会现形。所以每一处都留下断言，钉的是**成因**而不是现象——现象会变，成因不会。
//
// 事实经过（2026-10-18）：跑全量 --shots 矩阵逐窗口看图时，四处同时现形。
//
// ① GroupEditor「变量」空态提示落在表头行上
//    `VarsEmptyHint` 没写 Grid.Row，默认进 Row 0——而 Row 0 是那张 Auto 表头行。
//    于是这段提示和列标题挤在同一行里叠着画，头两行还互相压印（实测截图）。
//
// ② 折叠条标题被推到中间
//    Expander 的标题 TextBlock 是 `MaxWidth="560"` + 默认 Stretch。在 * 列里，
//    Stretch 撞上 MaxWidth 的结果是**把这一块居中**放进去，于是标题飘到窗口中间、
//    箭头留在行尾，中间空一大段——而样式自己的注释写着「标题顶格」。
//    表单里其它标签全在左轨上，只有这一行不在，读起来像另一个窗口。
//
// ③ IconPicker 顶部四行整片写着 "Syst"
//    品牌图标格的 Content 是一个 `Path`（图形对象），但没清 `ContentTemplate`。
//    全局 Button 样式那个 DataTemplate 是 `<TextBlock Text="{Binding}">`，
//    TextBlock 拿到 Path 就对它调 ToString()，画出类型名 System.Windows.Shapes.Path，
//    被 32px 的格子裁成 "Syst" / "em" 两行——一个 logo 都没有。
//    项目在 IconOnlyButton 的样式注释里已经记过同一颗钉子，另外两处格子也清了模板，
//    唯独这一处漏了。
//
// ④ SiteMapEditor 把「上移 / 下移」拆到了两行
//    四个按钮平铺在 WrapPanel 里，中文宽度下正好在两者之间折行。一对联动按钮被拆开，
//    ↓下移孤零零落到第二行。
public class RenderDefectRegressionTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Src(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root(), "app" }.Concat(parts).ToArray()));

    private static string StripComments(string src)
        => string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));

    // ① 空态提示必须显式落进内容那一行。
    // 写成「Hint 元素带 Grid.Row="1"」而不是「不要漏写」：漏写时的默认值是 0，
    // 而 0 恰恰是表头行——只有显式写出 1 才是对的。
    [Fact]
    public void The_variables_empty_hint_sits_in_the_content_row_not_the_header_row()
    {
        var xaml = Src("Views", "GroupEditorWindow.xaml");
        var at = xaml.IndexOf("x:Name=\"VarsEmptyHint\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "找不到 VarsEmptyHint");
        // 取这个元素自身的标签（到下一个 '>' 为止），别把后面兄弟节点一起吞进来。
        var tag = xaml.Substring(at, xaml.IndexOf('>', at) - at);
        Assert.Contains("Grid.Row=\"1\"", tag);
    }

    // ② 折叠条标题必须显式左对齐。MaxWidth 与 Stretch 同时存在时 WPF 会居中，
    // 而这个标题的全部意义就是和左边的表单标签落在同一条左轨上。
    [Fact]
    public void The_expander_header_stays_flush_left_under_a_max_width()
    {
        var theme = StripComments(Src("Theme.xaml"));
        var at = theme.IndexOf("x:Name=\"hdr\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "Theme.xaml 里找不到折叠条标题那个 TextBlock");
        var tag = theme.Substring(at, theme.IndexOf('>', at) - at);
        Assert.Contains("MaxWidth=", tag);
        Assert.Contains("HorizontalAlignment=\"Left\"", tag);
    }

    // ③ 品牌格子必须清掉默认 ContentTemplate。
    // 断言的形式是「凡是给 Button 赋了非字符串 Content 的方法，都必须同时清模板」，
    // 而不是只点名这一处——漏的是**一类**，只钉一个名字下一处照样漏。
    [Fact]
    public void Any_button_given_a_graphic_content_drops_the_default_content_template()
    {
        var code = StripComments(Src("Views", "IconPickerWindow.xaml.cs"));
        // 按方法切开：一个 Content = new TextBlock / 一个 Content = IconVisual.Brand(...)
        var methods = Regex.Split(code, @"(?=private Button Make)")
            .Where(m => m.Contains("Content = ", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(methods);
        foreach (var m in methods)
        {
            var name = Regex.Match(m, @"private Button (\w+)").Groups[1].Value;
            Assert.True(Contains(m, "ContentTemplate = null"),
                $"{name} 给按钮塞了图形 Content 却没有清 ContentTemplate —— " +
                "全局 Button 样式的 DataTemplate 是 <TextBlock Text=\"{Binding}\">，" +
                "TextBlock 会对图形调 ToString()，画出来的是类型名（实测整片 \"Syst\"）");
        }
    }

    private static bool Contains(string hay, string needle)
        => hay.Contains(needle, StringComparison.Ordinal);

    // ④ 上移/下移必须作为一个整体参与折行。
    [Fact]
    public void The_up_and_down_buttons_wrap_as_one_unit()
    {
        var xaml = Src("Views", "SiteMapEditorWindow.xaml");
        var at = xaml.IndexOf("Click=\"Up_Click\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "找不到上移按钮");
        // 从上移按钮往回找最近的一层容器：它必须落在一个横向 StackPanel 里，
        // 而不是与「删除」平铺在同一个 WrapPanel 上。
        var before = xaml.Substring(Math.Max(0, at - 400), 400);
        Assert.Contains("StackPanel Orientation=\"Horizontal\"", before);
        // 下移按钮要和上移同属这一组（中间只隔一个按钮的标记，不该再开新容器）。
        var between = xaml.Substring(at, xaml.IndexOf("Click=\"Down_Click\"", StringComparison.Ordinal) - at);
        Assert.DoesNotContain("StackPanel", between);
    }

    // ⑤ 截图矩阵不许拿本地化显示名当身份去找东西。
    // Templates 是 static readonly：名字在类型首次初始化那一刻就按当时的语言冻结，
    // 而矩阵每种语言都会切 CurrentUICulture。两边语言对不上时 First() 直接抛
    // InvalidOperationException，整轮矩阵中途夭折（实测，跑到一半 2226 张就断了）。
    [Fact]
    public void The_shots_matrix_identifies_templates_by_key_not_by_localized_name()
    {
        var code = StripComments(Src("DevChecks.cs"));
        Assert.DoesNotContain(".First(g =>", code);
        Assert.DoesNotContain("ActionGroupTemplates.All().First", code);
        Assert.Contains("ActionGroupTemplates.BuiltIn(", code);
    }
}
