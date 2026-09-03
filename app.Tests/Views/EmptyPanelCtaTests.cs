using System.IO;
using Xunit;

// 空面板上那颗行动按钮必须指向「管理面板」。
//
// 这是个真实回归：那段代码原本写的是「取 chromeOps 里的第一个」，在只有「管理面板」一个入口时
// 是对的；后来手势管理器加了进来、且排在它前面，空面板的按钮就悄悄变成了「鼠标手势」——
// 一块空面板叫你去配手势，和把它填满毫无关系。位置依赖不报错，只在别人调顺序时安静地指错地方。
//
// 空态是一次行动邀请：它指的那个地方，必须就是能解决"空"的那个地方。
public class EmptyPanelCtaTests
{
    private static string Src(params string[] parts)
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var all = new System.Collections.Generic.List<string> { d!.FullName, "app" };
        all.AddRange(parts);
        return File.ReadAllText(Path.Combine(all.ToArray()));
    }

    // 标记打在「管理面板」上，而不是别的入口上。
    [Fact]
    public void The_panel_manager_is_the_one_that_fills_an_empty_panel()
    {
        var app = Src("App.xaml.cs");
        Assert.Contains("Strings.Get(\"Panel_Manager\"), char.ConvertFromUtf32(0xE713), OpenPanelManager, fillsEmptyPanel: true", app);
        // 反面：手势入口不许带这个标记
        Assert.DoesNotContain("OpenGestureManager, fillsEmptyPanel", app);
    }

    // 而且选法必须认标记、不认顺序——否则下一个往前插入口的人会再踩一次。
    [Fact]
    public void The_choice_is_by_marker_not_by_order()
    {
        var panel = Src("Views", "QuickPanelWindow.xaml.cs");
        Assert.Contains("op.FillsEmptyPanel && _manage == null", panel);
    }
}
