using System;
using System.IO;
using System.Linq;
using Xunit;

// **钉住「面板管理器底部那两颗按钮是一对完整的动作按钮」这件事。**
//
// 事实经过（2026-10-20）：用户报管理面板底部的「清空点击统计」与「关闭」都「显示不完整」。
// 逐像素量过用户那张截图：两颗按钮的文字都完整落在按钮里（文字墨迹 17px 高，居中在 32 DIP 的
// 按钮内），按钮本身也都在窗口之内——**没有被裁**。真问题在版式：
//   ① 两颗按钮各占一行、各带各的右外边距，右缘差着 12 DIP（实测 973 与 991），
//      看着像两颗没对齐的补丁；
//   ② 指路那句话与「清空点击统计」同处一行，句子被右边那颗按钮挤掉大半，压成两行、
//      末字孤零零挂在第二行——而这与该文件自己写着的意图相反：注释里明写着
//      「它该有整行的宽度」。
//
// 所以这里钉的是**版式**：句子独占一行、两颗按钮同一行同一条右缘。
// 像素那一层交给截图矩阵（--shots 的 PanelManagerUsed 变体：点击统计有数据时那颗按钮才现形，
// 默认配置的截图永远拍不到它）。
public class PanelManagerFooterTests
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

    /// <summary>取某个具名元素在 XAML 里的那一个开始标签（含全部属性）。</summary>
    private static string Tag(string xaml, string name)
    {
        int i = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(i >= 0, $"XAML 里找不到 {name}");
        int open = xaml.LastIndexOf('<', i);
        int close = xaml.IndexOf('>', i);
        Assert.True(open >= 0 && close > open, $"{name} 不在一个开始标签里");
        return xaml.Substring(open, close - open + 1);
    }

    // ① 指路那句话独占一行。
    //
    // 它教的是**拖拽手势**——看界面看不出来的那一类，所以它需要整行的宽度。
    // 曾经它与「清空点击统计」同处一行：右边那颗按钮占着右侧一列，句子被挤成两行，
    // 末字孤零零挂在第二行（实测截图）。这一条钉的就是那个「同处一行」不许回来。
    [Fact]
    public void The_hint_gets_a_row_of_its_own()
    {
        var xaml = Xaml();
        Assert.Contains("Grid.Row=\"0\"", Tag(xaml, "ManagerHint"));
        // 动作按钮那一排在第 1 行——只要它回到第 0 行，句子就又被挤了。
        // （按钮自己没有 Grid.Row：它们是这一排的孩子，行号由这一排定。）
        Assert.Contains("Grid.Row=\"1\"", Tag(xaml, "FooterActions"));
    }

    // ② 两颗动作按钮收进**同一个**右对齐的横排，于是右缘同一条。
    //
    // 它们曾一个占第 0 行、一个占第 1 行，各带各的右外边距，右缘差着 12 DIP
    // （实测：清空点击统计 973、关闭 991）——看着像两颗没对齐的补丁。
    // 同理，两颗按钮必须都是这个横排的孩子；各挂各的容器，右缘就对不齐。
    [Fact]
    public void The_two_action_buttons_share_one_right_aligned_row()
    {
        var xaml = Xaml();
        var panel = Tag(xaml, "FooterActions");
        Assert.Contains("Orientation=\"Horizontal\"", panel);
        Assert.Contains("HorizontalAlignment=\"Right\"", panel);

        int i = xaml.IndexOf("x:Name=\"FooterActions\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("</StackPanel>", i, StringComparison.Ordinal);
        Assert.True(i >= 0 && end > i, "找不到 FooterActions 这一排的收尾");
        var block = xaml.Substring(i, end - i);
        Assert.Contains("x:Name=\"ResetUsageBtn\"", block);
        Assert.Contains("x:Name=\"CloseBtn\"", block);
    }

    // ③ 句子独占一行之后，它不必再给按钮让出一列：MaxWidth 就是主区宽度本身。
    //
    // 曾经是 main - btnCol - 14（要减去按钮那一列与它自己的右外边距）。
    // 按钮挪到下一行之后，btnCol 这个概念就不该再存在了——留着就说明两句又回到同一行去了。
    [Fact]
    public void The_hint_is_capped_at_the_full_width_of_the_main_area()
    {
        var code = Code();
        Assert.Contains("ManagerHint.MaxWidth = main;", code);
        Assert.DoesNotContain("btnCol", code);
    }

    // ④ 页脚只有一列。
    //
    // 多出来的列就是让句子与按钮抢位置的地方：右对齐的按钮要占住一列，句子就只剩 * 列，
    // 于是被压窄。按钮搬到下一行之后，页脚一列就够，两句并排的那条路也堵死了。
    [Fact]
    public void The_footer_has_a_single_column()
    {
        var xaml = Xaml();
        int grid = xaml.IndexOf("<Grid Grid.Row=\"1\"", StringComparison.Ordinal);
        Assert.True(grid >= 0, "找不到页脚那一格");
        int cols = xaml.IndexOf("<Grid.ColumnDefinitions>", grid, StringComparison.Ordinal);
        int end = xaml.IndexOf("</Grid.ColumnDefinitions>", cols, StringComparison.Ordinal);
        Assert.True(cols >= 0 && end > cols, "找不到页脚的列定义");
        var block = xaml.Substring(cols, end - cols);
        Assert.DoesNotContain("Auto", block);
        Assert.Contains("\"*\"", block);
    }
}
