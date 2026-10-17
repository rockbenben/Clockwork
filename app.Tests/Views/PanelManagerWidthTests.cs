using System;
using System.IO;
using System.Linq;
using Xunit;

// **钉住「面板管理器右边不出现黑边」这件事的三个成因。**
//
// 事实经过（2026-10-17）：用户报面板管理器右侧有一条黑边。卡片的宽度由格子阵算出
// （PanelMetrics.GridWidth），不随窗口长——窗口比卡片宽出来的那一截就是黑边。
//
// 先后试错了三版才找齐全部成因，每一版都只解决了一部分：
//   ① 把窗口从 880 缩到 720 —— 只是把黑边变窄，没有去掉。
//   ② 只给外观条加 MaxWidth 封顶 —— 毫无作用，窗口默认宽度写死 720，仍开在 720。
//   ③ 改用 SizeToContent="Width,Height" —— 宽度反而涨到 967：**SizeToContent 量子元素时
//      给的可用宽度是无限的**，于是三样东西各按整行宽度报给外层，把窗一路撑开。
//      实测（4 列标准档，卡片 360）：外观条单行 468、顶栏页签随分类数增长、
//      页脚那句 TextWrapping="Wrap" 的指路文字 966。卡片不跟着长，多出来的全是黑边。
//
// 还有一个更隐蔽的：卡片那一列原来是 `*`，它会吞掉所有富余宽度再由 HorizontalAlignment
// 平分到卡片两边，于是黑边在左右各出现一半，看着像「设计如此」。改成 Auto 之后这一列
// 恰好等于卡片，问题才真正只剩「谁把窗口撑宽」。
//
// 为什么要有这组断言：黑边在截图里一眼能看见，但**看不见的是成因**——三处 MaxWidth 少一处
// 就会安静地长回来，而且长得毫无征兆（不像崩、不像报错，只是一条白边）。人眼 review 会忘。
public class PanelManagerWidthTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Xaml() => File.ReadAllText(Path.Combine(Root(), "app", "Views", "PanelManagerWindow.xaml"));

    private static string Code()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "app", "Views", "PanelManagerWindow.xaml.cs"));
        // 剥掉注释再断言：本项目的注释会原样引用被断言的那几行，不剥的话删掉代码只留注释也照样绿。
        return string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
    }

    // 成因 ①：窗口不许写死 Width。Window.Width 是含 OS 边框的外框尺寸，写死它 SizeToContent 就失效，
    // 而且内容会比算出来的少掉边框那么宽，卡片反被挤出一截滚动条。
    [Fact]
    public void The_window_lets_layout_pick_the_width()
    {
        var xaml = Xaml();
        var open = xaml.IndexOf("<Window ", StringComparison.Ordinal);
        var tag = xaml.Substring(open, xaml.IndexOf('>', open) - open);
        Assert.Contains("SizeToContent=\"Width,Height\"", tag);
        Assert.DoesNotContain("Width=\"", tag.Replace("MinWidth=\"", "").Replace("MaxWidth=\"", ""));
    }

    // 成因 ②：卡片那一列必须是 Auto。`*` 会吞掉窗口多出来的宽度，再由居中平分到卡片两边——
    // 黑边于是左右各一半，看着像版面对称设计，其实是没画完。
    [Fact]
    public void The_card_column_does_not_absorb_the_extra_width()
    {
        var xaml = Xaml();
        // 卡片那个 ScrollViewer 所在 Grid 的两列定义。
        var at = xaml.IndexOf("<!-- 顶栏压在最上面并跨满两列", StringComparison.Ordinal);
        Assert.True(at >= 0, "找不到面板管理器主区的栅格定义");
        var block = xaml.Substring(at, 400);
        Assert.DoesNotContain("\"*\"", block);
        Assert.Contains("Auto", block);
    }

    // 成因 ③：三个会在无限宽度下报出整行宽度的元素，每个都必须被 CapWidthToContent 封顶。
    // 少一个就长一条黑边，而且长得毫无征兆。
    [Theory]
    [InlineData("AppearanceBar")]   // 外观控制条（WrapPanel，单行约 468）
    [InlineData("TopTabBar")]       // 顶栏分类页签（WrapPanel，分类一多就长）
    [InlineData("ManagerHint")]     // 页脚那句指路文字（Wrap 下不折行，约 966）
    public void The_element_that_leaks_its_full_row_width_is_capped(string name)
    {
        // 必须真的有这个名字（改了 XAML 就该一起改这里）。
        Assert.Contains($"x:Name=\"{name}\"", Xaml());
        Assert.Contains($"{name}.MaxWidth =", Code());
    }

    // 封顶值必须随版面/格子档位/语言变，不能写死成某个数字：曾经 MaxWidth="620" 是死的，
    // 内容更窄时它反而把窗口撑着。现在三个上限都由同一个 main 推出来。
    [Fact]
    public void The_cap_is_computed_from_the_rail_and_the_grid_not_hardcoded()
    {
        var code = Code();
        Assert.Contains("rail + 10 + card", code);
        Assert.Contains("PanelMetrics.GridWidth(", code);
        // 外层 Border 的内边距要减掉，否则 WrapPanel 报 main 宽、Border 报 main+26，
        // 窗口还是比卡片宽一截（这一条曾经单独造成 26px 的黑边）。
        Assert.Contains("AppearanceBar.MaxWidth = main - 26", code);
    }

    // 量左栏不能用 ActualWidth：构造函数里调到这里时整扇窗还没显示，ActualWidth 恒为 0，
    // 于是永远走兜底的 140px，而真实左栏约 42px——多出来的 98px 直接变成黑边。
    [Fact]
    public void The_rail_is_measured_before_the_window_is_shown()
    {
        var code = Code();
        Assert.Contains("RailScroll.Measure(new System.Windows.Size(double.PositiveInfinity", code);
        Assert.Contains("RailScroll.DesiredSize.Width", code);
    }

    // 显示之后要重量一次：构造时左栏的页签刚填上去，量到的未必是最终那一份
    // （换语言、换版面都会让页签变宽）。
    [Fact]
    public void The_cap_is_recomputed_once_the_window_is_loaded()
    {
        var code = Code();
        Assert.Contains("Loaded += (_, _) => CapWidthToContent();", code);
    }
}
