using System;
using System.IO;
using System.Linq;
using Xunit;

// 动作组编辑器左侧「模块工具箱」的无障碍名。
//
// 每一项是一个 record（ToolItem），WPF 给 ListBoxItem 取名时退到 **record 的 ToString()**——
// 于是读屏软件念出来的是一整串 C# 记录转储：
// 「ToolItem { Label = 复制, Kind = keys, Seed = Clockwork.Core.LaunchStep, Glyph = }」。
// 工具箱是动作编辑器里加步骤的主要入口，这一列对读屏用户等于不可用。
//
// 同一页的分类下拉早就踩过同一个坑（XAML 里 DisplayMemberPath 那条注释：ComboBox 渲染的是
// record 的 ToString()），那只在下拉里补了 DisplayMemberPath，旁边的 ListBox 漏了。
//
// 这里断言 XAML 源码而不是建那扇窗：窗要 Application 的资源字典才建得起来
// （测试宿主里没有 Application，Theme.xaml 的 StaticResource 全都解不开），
// 所以本仓库里与窗口版面有关的断言一律走源码——见 PanelManagerWidthTests /
// RenderDefectRegressionTests 的同一手法。
public class ToolboxAccessibleNameTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // 剥掉注释再断言：本项目的注释会原样引用被断言的那几行，不剥的话删掉代码只留注释也照样绿。
    private static string Xaml()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "app", "Views", "GroupEditorWindow.xaml"));
        return string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
    }

    // 名字取自 Label：每一项念出来就是它自己的名字（复制 / 粘贴 / …）。
    [Fact]
    public void Every_toolbox_item_is_named_after_its_own_label()
    {
        var xaml = Xaml();
        Assert.Contains("AutomationProperties.Name\" Value=\"{Binding Label}\"", xaml);
    }

    // 而且是给**容器**（ListBoxItem）设的，不是给模板里那个 TextBlock 设的：
    // 给 TextBlock 设的话，WPF 仍然拿 record 的 ToString() 当 ListBoxItem 的名字。
    [Fact]
    public void The_name_is_set_on_the_item_container_not_on_the_template()
    {
        var xaml = Xaml();
        Assert.Contains("<ListBox.ItemContainerStyle>", xaml);
        Assert.Contains("TargetType=\"ListBoxItem\"", xaml);
    }

    // 换掉容器样式不能换掉主题那份：Theme.xaml 里的 ListBoxItem 样式带着选中/悬停的模板，
    // 丢了它整列的选中高亮就没了（那一列是加步骤时最常点的地方）。
    [Fact]
    public void The_toolbox_item_style_still_starts_from_the_theme_one()
    {
        var xaml = Xaml();
        Assert.Contains("BasedOn=\"{StaticResource {x:Type ListBoxItem}}\"", xaml);
    }
}
