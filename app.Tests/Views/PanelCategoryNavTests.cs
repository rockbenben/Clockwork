using System;
using System.IO;
using System.Linq;
using Xunit;

// 面板的两条导航栏：**上边选分类，左边列这一类里的页**——父子，不是平行的两处。
//
// 这一轮钉三件事，每一件都对着一个真症状：
//
//   ① 顶栏点了另一个分类，左栏必须跟着换。
//      曾经它是坏的：JumpToCategory 先把 _cat 改成目标类，再 ShowPage；
//      而 ShowPage → PaintRails → SyncCategory 认的是「**当前页**属于哪一类」，
//      看到 _cat 已经等于目标类，就以为「类别没变」提前返回，左栏于是不重建——
//      格子换到了那一类，左栏却还列着上一类的页（用户实报：
//      「切换横分类时竖栏没有对应内容」）。
//
//   ② 层级怎么说明白：**管理器**在两条栏顶上写明「分类」「页」，**快捷面板**不写字、
//      只把左栏整体缩进（用户实报过「面板上『页』『分类』这个属性名不该显示」——
//      那两个词从面板上撤了，只留在管理器里）。
//
//   ③ 顶栏那颗「+」建出来的分类**站得住**：分类是从页上涌现的，
//      一类必须带着一张页才站得住，否则它在配置里没有容身之处，关掉管理器就没了；
//      而它带的是一张**空页**，一张已有页都不挪走，新的一类还排在顶栏末尾。
//
// **这里钉的是接线与结构，效果由进程内自查点一遍**（app/DevCategoryNav.cs）：
// 真窗口的检查跑在真进程里——单元测试进程里不能建 WPF Application
// （手势那一族测试明确依赖「没有 Application」，造一个会让它们跨线程炸掉）。
public class PanelCategoryNavTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!.FullName;
    }

    private static string Src(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root(), "app" }.Concat(parts).ToArray()));

    // 取某个方法/属性的那段源码。**剥掉注释**：本项目的注释会原样引用被断言的那几行，
    // 不剥的话删掉代码只留注释也照样绿。
    private static string Body(string src, string anchor)
    {
        int at = src.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at >= 0, $"源码里找不到 {anchor}：这段守卫要跟着它走");
        int end = src.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > at, $"{anchor} 后面没有收尾");
        var body = src.Substring(at, end - at);
        return string.Join("\n", body.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
    }

    // ① 换类只能由 SyncCategory 做（它认的是当前页属于哪一类）。
    //
    // JumpToCategory 若自己先把 _cat 改成目标类，SyncCategory 看到「类别没变」
    // 就提前返回，左栏永远不重建 —— 格子换到了那一类，左栏还列着上一类的页。
    [Fact]
    public void The_top_row_does_not_switch_the_category_on_its_own()
    {
        var body = Body(Src("Views", "QuickPanelWindow.xaml.cs"), "void JumpToCategory()");
        Assert.DoesNotContain("_cat =", body);
        // 它只负责把页面换过去；换类与左栏重建是 SyncCategory 的事。
        Assert.Contains("ShowPage(", body);
    }

    // SyncCategory 认的是**当前页**：它比的是「当前页属于哪一类」与「当前类」。
    // 少了这一比，它就成了「自己跟自己比」——永远相等，于是永远不重建左栏。
    [Fact]
    public void The_category_follows_the_page_that_is_current()
    {
        var body = Body(Src("Views", "QuickPanelWindow.xaml.cs"), "private void SyncCategory()");
        Assert.Contains("CatOf(_pages[_page])", body);
        Assert.Contains("BuildPageRail();", body);
    }

    // ② 层级怎么说明白：**管理器写字，快捷面板不写字**。
    //
    // 用户实报过「面板上『页』『分类』这个属性名不该显示」——面板每一像素都得留给格子，
    // 层级改由缩进说（XAML 里左栏整体右移）；管理器是编辑窗口，写得下那两个字。
    [Fact]
    public void Only_the_manager_says_what_the_rows_hold()
    {
        Assert.Contains("BarHead(Strings.Get(\"Panel_Category\"))", Src("Views", "PanelManagerWindow.xaml.cs"));
        Assert.Contains("x:Name=\"RailHead\"", Src("Views", "PanelManagerWindow.xaml"));

        Assert.DoesNotContain("BarHead(", Src("Views", "QuickPanelWindow.xaml.cs"));
        Assert.DoesNotContain("RailHead", Src("Views", "QuickPanelWindow.xaml.cs"));
        Assert.DoesNotContain("RailHead", Src("Views", "QuickPanelWindow.xaml"));
    }

    // ③ 顶栏那颗「+」：新的一类里放一张**空页**，一张已有页都不挪走，
    // 而且新的一类排在末尾（顶栏的先后就是页的先后）。
    //
    // 「只建一类不建页」建出来的东西在配置里没有容身之处（分类是从页上涌现的），
    // 关掉管理器就没了；而「把当前这一页挪进去」又是另一种越界——
    // 换分类是页眉那颗按钮与拖页签两条路的事（用户实报：
    // 「新分类的页应该默认为空的，不该把其他分类的页直接移过来」、
    // 「新分类不该放在最前面」）。
    [Fact]
    public void The_new_category_starts_with_an_empty_page_and_moves_nothing()
    {
        var body = Body(Src("Views", "PanelManagerWindow.xaml.cs"), "private void NewCategory()");
        // 新页：分类唯一的容身之处（空页，往里放动作）。
        Assert.Contains("new PanelPage", body);
        // 追加在列表末尾 —— 新的一类因此排在顶栏末尾，而不是把某一页从队首挪走。
        Assert.Contains("_config.PanelPages.Add(page);", body);
        // 一张已有页都不许被挪走。
        Assert.DoesNotContain("page.Tab = use;", body);
        Assert.Contains("Commit();", body);
    }

    // 半成品分类那套机制（「只在内存里活着的那一类」）不许回来：
    // 它是那个 bug 的本体——顶栏上有一格，配置里没有它。
    [Fact]
    public void No_category_lives_only_in_memory()
    {
        var src = Src("Views", "PanelManagerWindow.xaml.cs");
        Assert.DoesNotContain("_pendingCat", src);
    }
}
