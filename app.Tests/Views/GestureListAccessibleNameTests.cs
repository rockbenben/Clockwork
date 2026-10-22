using System;
using System.IO;
using System.Linq;
using Xunit;

// 手势管理器那一列的无障碍名。
//
// 每一行的内容是**画出来的**（笔迹缩略图是一段 Path 几何 + 一行文字），
// WPF 从 Content 取名字取不到字符串，于是每一行只报「System.Windows.Controls.ListBoxItem」——
// 读屏用户听到的是一列没有名字的行，分不清哪条是哪条。
//
// 而 Row.ToString() 本来就是为朗读写的（见它的注释：「笔迹缩略图是给眼睛的，
// 念出来仍要是一句完整的话——一条画出来的线没法朗读」）：箭头 + 完整摘要。
// 那一行 ToString() 此前从没被用上——名字得显式给。
//
// 这里断言源码而不是建那扇窗：窗要 Application 的资源字典才建得起来
// （测试宿主里没有 Application，Theme.xaml 的 StaticResource 全都解不开），
// 所以本仓库里与窗口有关的断言一律走源码——见 PanelManagerWidthTests 的同一手法。
public class GestureListAccessibleNameTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Code()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "app", "Views", "GestureManagerWindow.xaml.cs"));
        // 剥掉注释再断言：本项目的注释会原样引用被断言的那几行，不剥的话删掉代码只留注释也照样绿。
        return string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
    }

    [Fact]
    public void Every_gesture_row_is_given_an_accessible_name()
    {
        var code = Code();
        Assert.Contains("AutomationProperties.SetName(item,", code);
    }

    // 名字取的是 Row.ToString()（箭头 + 摘要），不是类型名、也不是空串。
    [Fact]
    public void The_accessible_name_is_the_row_its_own_readable_text()
    {
        var code = Code();
        Assert.Contains("AutomationProperties.SetName(item, r.ToString())", code);
    }
}
