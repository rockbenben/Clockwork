using System;
using System.IO;
using System.Linq;
using Xunit;

// **钉住语境开关只有动作编辑器一处打开**，以及菜单那一层真的会筛。
//
// 这一版出过两次同类问题，所以这里钉的是**接线**而不是行为：第一次只改了编辑器里的下拉，
// 菜单还是全量；第二次面板分流只做在管理器里，浮动面板那条路原样直通「运行程序」。
// 两次都能编译、都能过全量测试，只有用户点下去才知道。
//
// 「打开口」只有一处，是让下一次漏改在构建期就红：多一处入口就多一条「这里没筛」的路。
public class StepContextWiringTests
{
    private static string Src(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    private static string[] AppSources()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        return Directory.EnumerateFiles(Path.Combine(d!.FullName, "app"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();
    }

    // 菜单那一层必须真的筛。编辑器筛了而菜单没筛，用户点的第一下仍然是 29 项。
    [Fact]
    public void The_add_step_menu_filters_by_context_too()
    {
        var src = Src(Path.Combine("app", "Views", "StepMenu.cs"));
        Assert.Contains("bool insideGroup = false", src);
        Assert.Contains("if (!insideGroup && !StepDisplay.IsAtomic(kind)) continue;", src);
        // 筛空的节整节不显示：留一个点开什么都没有的子菜单比不显示更糟。
        Assert.Contains("if (items.Count == 0) continue;", src);
    }

    // 浮动面板的加格曾经直通「运行程序」编辑器——面板上编复杂动作的那条路只存在于管理器里。
    [Fact]
    public void The_floating_panel_offers_the_compound_branch_too()
    {
        var src = Src(Path.Combine("app", "Services", "PanelCoordinator.cs"));
        Assert.Contains("Panel_NewCompound", src);
        // 旧写法是直接开编辑器；它一旦回来，上面的分流就等于不存在。
        Assert.DoesNotContain("Edit(owner, null, \"app\"", src);
    }

    // 只有动作编辑器打开这个口。**四处单步语境**（启动清单 / 面板管理器 / 浮动面板 / 手势）
    // 都必须是筛过的那一侧。
    [Fact]
    public void Only_the_action_editor_opens_the_full_catalogue()
    {
        var hits = AppSources()
            .SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l)))
            .Where(x => x.Line.Contains("insideGroup: true"))
            .ToList();
        Assert.All(hits, x => Assert.Equal("GroupEditorWindow.xaml.cs", x.File));
        Assert.True(hits.Count >= 2, $"动作编辑器里两处调用应当都开着，却只找到 {hits.Count} 处");
    }
}
