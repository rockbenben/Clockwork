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

    // 成因 ②：主区那几列必须都是 Auto。`*` 会吞掉窗口多出来的宽度，再由居中平分到卡片两边——
    // 黑边于是左右各一半，看着像版面对称设计，其实是没画完。
    //
    // 现在主区是三列（左栏 / 卡片 / 外观列），三列都必须是 Auto：任何一列写成 `*`，
    // 窗口多出来的那一截就会进那一列，再由居中平分到卡片两边——黑边又回来了。
    [Fact]
    public void The_card_column_does_not_absorb_the_extra_width()
    {
        var xaml = Xaml();
        // 主区那一格（外面那个 Grid 的第 0 行）里的列定义。
        int grid = xaml.IndexOf("<Grid Grid.Row=\"0\"", StringComparison.Ordinal);
        Assert.True(grid >= 0, "找不到面板管理器的主区");
        int cols = xaml.IndexOf("<Grid.ColumnDefinitions>", grid, StringComparison.Ordinal);
        int end = xaml.IndexOf("</Grid.ColumnDefinitions>", cols, StringComparison.Ordinal);
        Assert.True(cols >= 0 && end > cols, "找不到主区的列定义");
        var block = xaml.Substring(cols, end - cols);
        Assert.DoesNotContain("\"*\"", block);
        Assert.Contains("Auto", block);
    }

    // 成因 ③：还在按「无限宽」报出自己那一行整行宽度的元素，每个都必须被 CapWidthToContent 封顶。
    // 少一个就长一条黑边，而且长得毫无征兆。
    //
    // **外观控制条不再在这张表里。** 它从前是那条横排的 WrapPanel（约 468px 的单行），
    // 是整扇窗最宽的东西——比卡片还宽，于是它把窗撑成竖长条（实测 521×586，宽高比 0.89），
    // 而卡片只占中间一小块。现在它竖在卡片右边那一列里，宽度由内容定、不再报整行，
    // 于是它从「撑宽窗口的那一个」变成了「窗口宽度的一部分」。
    [Theory]
    [InlineData("TopTabBar")]       // 顶栏分类页签（WrapPanel，分类一多就长）
    [InlineData("ManagerHint")]     // 页脚那句指路文字（Wrap 下不折行，约 966）
    public void The_element_that_leaks_its_full_row_width_is_capped(string name)
    {
        // 必须真的有这个名字（改了 XAML 就该一起改这里）。
        Assert.Contains($"x:Name=\"{name}\"", Xaml());
        Assert.Contains($"{name}.MaxWidth =", Code());
    }

    // 外观控制条**必须在卡片右边那一列**，不能再回到卡片底下那条横条。
    //
    // 这条钉的是形状，不是文案：横排那条自己就有一行那么宽，它成了整扇窗最宽的东西，
    // 于是窗被一条设置栏撑成竖长条（实测 521×586），卡片却只占中间一小块。
    // 竖过来之后窗宽由「左栏 + 卡片 + 外观列」相加决定（实测 738×478，宽高比 1.55），
    // 横条腾出来的那九十像素全部让给卡片——卡片本来就被切掉了一半。
    [Fact]
    public void The_appearance_controls_sit_beside_the_card_not_below_it()
    {
        var xaml = Xaml();
        int box = xaml.IndexOf("x:Name=\"AppearanceBox\"", StringComparison.Ordinal);
        Assert.True(box >= 0, "找不到外观列：它得在卡片右边，不在卡片底下");
        // 它的行与列：Grid.Row="1" Grid.Column="2"（第三列）。
        var tag = xaml.Substring(box, 260);
        Assert.Contains("Grid.Column=\"2\"", tag);
        // 底下那条横条不许回来：AppearanceBar 那个名字一出现，就是它又变回横排了。
        Assert.DoesNotContain("AppearanceBar", xaml);
        Assert.DoesNotContain("AppearanceBar", Code());
    }

    // 封顶值必须随版面/格子档位/语言变，不能写死成某个数字：曾经 MaxWidth="620" 是死的，
    // 内容更窄时它反而把窗口撑着。现在三个上限都由同一个 main 推出来。
    [Fact]
    public void The_cap_is_computed_from_the_rail_and_the_grid_not_hardcoded()
    {
        var code = Code();
        Assert.Contains("rail + 10 + card", code);
        Assert.Contains("PanelMetrics.GridWidth(", code);
        // 外观列也算进窗口宽度：那一列是内容的一部分，漏了它窗口就比内容窄一截。
        Assert.Contains("+ 14 + look", code);
        // 外观列的宽度由它的**内容**量出来，而且要先把竖滚动条的那一格留出来——
        // 滚动条一出现就占掉视口，视口比内容窄就把内容从右边切掉一截。
        Assert.Contains("AppearanceCol.Measure(", code);
        Assert.Contains("SystemParameters.VerticalScrollBarWidth", code);
        Assert.Contains("AppearanceBox.Width = look", code);
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
