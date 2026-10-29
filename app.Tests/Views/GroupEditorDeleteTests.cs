using System;
using System.IO;
using System.Linq;
using Clockwork.Core;
using Clockwork.Views;
using Xunit;

// 「删除」按钮按下后要删哪些步骤。
//
// 实际发生过（issue #5，v3.2.1）：SDel_Click 把「目标的后代」当成删除集，
// 于是普通步骤（没有后代）删的是一个空集——按钮点了像没反应；块则删掉孩子、
// 留下一个空壳。判据是纯数据，直接测；窗口本身不在这里构造（同 StepEditorAdvancedTests）。
public class GroupEditorDeleteTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void A_leaf_step_is_deleted_on_its_own()
    {
        var leaf = new LaunchStep { Id = "leaf", Kind = "app", Target = "notepad.exe" };
        var sibling = new LaunchStep { Id = "sibling", Kind = "app", Target = "calc.exe" };

        var set = GroupEditorWindow.DeleteSet(leaf, new[] { leaf, sibling });

        Assert.Equal(new[] { leaf }, set);
    }

    [Fact]
    public void A_block_deletes_itself_and_every_descendant()
    {
        var block = new LaunchStep { Id = "block", Kind = "each" };
        var child = new LaunchStep { Id = "child", Kind = "app", ParentId = "block" };
        var grandchild = new LaunchStep { Id = "grandchild", Kind = "app", ParentId = "child" };
        var sibling = new LaunchStep { Id = "sibling", Kind = "app" };
        var steps = new[] { sibling, block, child, grandchild };

        var set = GroupEditorWindow.DeleteSet(block, steps);

        Assert.Equal(new[] { block, child, grandchild }, set);
    }

    // 真·嵌套：**块里还有块**。
    //
    // 上面那条的 grandchild 挂在 child 下面，而 child 是 Kind="app" —— 「一个普通步骤挂着孩子」
    // 这种形状 UI 造不出来：ParentIdForNewStep 只把新步骤挂到**块**下面（IsBlockKind 判定），
    // 那个 app 步骤连块都不是。
    //
    // 生产里真会出现的形状是块套块：选住一个循环 → 新增，新步骤就挂进这个循环，而新步骤自己
    // 也可以是一个循环（StepTree 的 Scope 存档就是为嵌套准备的）。这条把删除集钉在**那个形状**上：
    // 外面的循环、里面的循环、里面那一步，一起删。
    //
    // 与上面那条的区别只在夹具：两条都会在「Walk 不再递归」时变红（实测去掉递归，两条一起红），
    // 但上面那条钉的是一个造不出来的配置——将来收树规则若变了，它会因为夹具失效而红，
    // 届时要改的是夹具、不是这条守卫。这条钉的是真形状，规则怎么变它都还在测「删一段就删掉整段」。
    //
    // 让它变红的改动：Walk 不再递归（只收一层孩子）。
    [Fact]
    public void A_block_nested_in_a_block_goes_with_it()
    {
        var outer = new LaunchStep { Id = "outer", Kind = "each" };
        var inner = new LaunchStep { Id = "inner", Kind = "each", ParentId = "outer" };
        var leaf = new LaunchStep { Id = "leaf", Kind = "app", ParentId = "inner", Target = "notepad.exe" };
        var sibling = new LaunchStep { Id = "sibling", Kind = "app" };

        var set = GroupEditorWindow.DeleteSet(outer, new[] { outer, inner, leaf, sibling });

        Assert.Equal(new[] { outer, inner, leaf }, set);
        // 块外面的那一步不在删除集里：删的是「这一段」，不是「这个组」。
        Assert.DoesNotContain(sibling, set);
    }

    // 上面那条只钉判据；这一条钉「处理器真的用了它」——只改 DeleteSet、不改接线，
    // 按钮仍然会删空集。剥掉注释再断言，避免解释性注释里的原样引用把测试糊绿。
    [Fact]
    public void The_delete_button_removes_the_target_as_well_as_its_children()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Views", "GroupEditorWindow.xaml.cs"));
        var code = string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
        int at = code.IndexOf("private void SDel_Click", StringComparison.Ordinal);
        Assert.True(at >= 0, "找不到 SDel_Click——它被改写/改名了，这条测试要跟着改。");
        int end = code.IndexOf("private IEnumerable<LaunchStep> DescendantsOf", at, StringComparison.Ordinal);
        Assert.True(end > at);
        var body = code.Substring(at, end - at);

        Assert.Contains("DeleteSet(target", body);
        Assert.Contains("RemoveSteps(doomed)", body);
        Assert.DoesNotContain("RemoveSteps(DescendantsOf", body);
    }
}
