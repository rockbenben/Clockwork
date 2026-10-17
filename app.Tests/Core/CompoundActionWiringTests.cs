using System;
using System.IO;
using System.Linq;
using Xunit;

// **钉住「新建复杂动作」的三条入口都是完整的一笔。**
//
// 它们分散在三处（面板管理器 / 浮动面板 / 手势管理器），而缺的那一半不会报错：
// 只建引用不落配置 → 格子指向一个不存在的动作，运行时按坏引用处理，用户看不出是哪一步；
// 只落配置不建引用 → 动作建好了，没人在任何地方用它，而这一趟点击白做了。
// 两条都只有点下去才知道，所以这里按「三处都得两样齐全」来钉。
//
// 与 StepContextWiringTests 的分工：那边钉「语境开关只有一处开口」，这边钉「复杂动作这笔不缺半」。
public class CompoundActionWiringTests
{
    private static string Src(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    // 三处入口。少一处就是少了一条「在面板上手边编一个动作」的路。
    public static TheoryData<string> CompoundSites => new()
    {
        Path.Combine("app", "Views", "PanelManagerWindow.xaml.cs"),
        Path.Combine("app", "Services", "PanelCoordinator.cs"),
        Path.Combine("app", "Views", "GestureManagerWindow.xaml.cs"),
    };

    [Theory]
    [MemberData(nameof(CompoundSites))]
    public void A_new_compound_action_is_saved_and_referenced(string rel)
    {
        var src = Src(rel);
        // 建出来的那一个要落进配置，否则格子/手势指的是空气。
        // 只认「ActionGroups.Add(g)」这一段：接在 _config 还是接在局部 config 上都行，
        // 那是写法不是语义——把接收者也钉进去，只会让改个局部变量名的重构红一次。
        Assert.Contains("ActionGroups.Add(g)", src);
        // 还要有一枚 group 引用指向它，否则这一趟点击什么也没挂上。
        Assert.Contains("new LaunchStep { Kind = \"group\", GroupId = g.Id }", src);
        // 取消编辑不能留半个：g == null 就不许往下走。
        Assert.Contains("if (g == null) return;", src);
    }

    // 动作标签页那条「把选中的几行组合成一个」是第四个入口，走的是既有 Add 而不是新开编辑器。
    [Fact]
    public void Combining_selected_rows_references_rather_than_copies()
    {
        var src = Src(Path.Combine("app", "MainWindow.xaml.cs"));
        Assert.Contains("new LaunchStep { Kind = \"group\", GroupId = g.Id }", src);
        // 存的是 id：原动作之后改了，组合里的那一段要跟着变。
        // 换成整份拷贝的话这一条要改成别的断言——先钉住「不是拷贝」。
        Assert.DoesNotContain("ConfigStore.DeepClone(g)", src);
    }
}
