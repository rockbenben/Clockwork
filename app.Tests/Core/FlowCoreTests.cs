using Clockwork.Core;
using Xunit;

// 块结构（ParentId）、列表变量、算术、键名表。
// 执行侧（each / skip / 作用域）的用例在 FlowRunnerTests —— 那份跑的是真的 ActionGroupRunner。

public class FlowCoreTests
{
    private static LaunchStep S(string kind, string parent = "", string id = "")
        => new() { Id = id.Length > 0 ? id : kind + "-" + Guid.NewGuid().ToString("N")[..6], Kind = kind, ParentId = parent };

    // ---- 层级 ----

    [Fact]
    public void Children_hang_off_their_block_and_top_stays_flat()
    {
        var block = S("each", id: "b1");
        var c1 = S("delay", parent: "b1");
        var c2 = S("delay", parent: "b1");
        var top = S("message");

        var tree = StepTree.Build(new[] { block, c1, top, c2 });
        // 顶层 = 块本身 + 块后面那句。块自己也在顶层（它是那一段的起点），
        // 断言只写「top 在里面」而不是「顶层只有 top」——后者是这条测试自己写错过一次。
        Assert.Contains(top, tree.Top);
        Assert.Contains(block, tree.Top);
        Assert.Equal(new[] { c1, c2 }, tree.ChildrenOf("b1"));
    }

    [Fact]
    public void An_empty_block_simply_has_no_children()
    {
        var block = S("each", id: "b1");
        var tree = StepTree.Build(new[] { block });
        Assert.Empty(tree.ChildrenOf("b1"));
        Assert.Empty(tree.ChildrenOf("nothing-like-this"));
        Assert.Empty(tree.ChildrenOf(null));
    }

    // 父 id 指向一个不存在的步骤：留在原地但当作顶层。运行期若不兜住，它永远不会被执行到，
    // 而界面上显示得好好的。
    [Fact]
    public void A_dangling_parent_becomes_top_level_rather_than_vanishing()
    {
        var orphan = S("delay", parent: "no-such-block");
        var tree = StepTree.Build(new[] { orphan });
        Assert.Same(orphan, Assert.Single(tree.Top));
    }

    // 环会让递归执行栈溢出，而栈溢出在动作组里表现为整个进程崩掉——配置里那一行完全看不出问题。
    [Theory]
    [InlineData(true)]     // 自己指自己
    [InlineData(false)]    // 两个互指
    public void A_parent_cycle_is_broken_instead_of_overflowing_the_stack(bool selfParent)
    {
        var a = S("each", selfParent ? "" : "", id: "a");
        var b = S("each", id: "b");
        a.ParentId = selfParent ? "a" : "b";
        b.ParentId = selfParent ? "" : "a";

        var tree = StepTree.Build(new[] { a, b });
        // 不断言具体谁被提上来，只断言「建树能完成、且每个人在某处只出现一次」——
        // 哪一种解法都能接受，不能接受的是爆栈或重复执行。
        var all = tree.Top.Concat(tree.ChildrenOf("a")).Concat(tree.ChildrenOf("b")).ToList();
        Assert.Equal(all.Select(x => x.Id).Distinct().Count(), all.Count);
    }

    [Fact]
    public void Nesting_two_levels_deep_keeps_the_right_parent()
    {
        var outer = S("each", id: "o");
        var inner = S("each", parent: "o", id: "i");
        var leaf = S("delay", parent: "i");

        var tree = StepTree.Build(new[] { outer, inner, leaf });
        Assert.Same(outer, Assert.Single(tree.Top));
        Assert.Same(inner, Assert.Single(tree.ChildrenOf("o")));
        Assert.Same(leaf, Assert.Single(tree.ChildrenOf("i")));
    }

    [Fact]
    public void A_block_inside_a_block_still_only_lists_its_own_children()
    {
        var outer = S("each", id: "o");
        var inner = S("each", parent: "o", id: "i");
        var after = S("delay");
        var tree = StepTree.Build(new[] { outer, inner, after });
        Assert.Equal(2, tree.Top.Count);                 // 外层块 + 块后面的那句
        Assert.Equal(new[] { inner }, tree.ChildrenOf("o"));
    }

    // ---- 列表变量与作用域 ----

    [Fact]
    public void A_list_variable_is_read_back_as_a_list()
    {
        var v = new RunVars();
        v.SetList("lines", new[] { "a", "b", "c" });
        Assert.Equal(new[] { "a", "b", "c" }, v.GetList("lines"));
        // 同一个名字不该既是文本又是列表：每个读它的人都要先猜这一轮拿到哪种，而猜错的那支看不出来。
        Assert.Null(v.Get("lines"));
    }

    [Fact]
    public void Setting_text_over_a_list_name_clears_the_list_and_vice_versa()
    {
        var v = new RunVars();
        v.SetList("x", new[] { "1" });
        v.Set("x", "hello");
        Assert.Null(v.GetList("x"));
        Assert.Equal("hello", v.Get("x"));

        v.SetList("x", new[] { "2" });
        Assert.Null(v.Get("x"));
    }

    [Fact]
    public void Scope_restores_only_the_names_it_was_given()
    {
        var v = new RunVars();
        v.Set("item", "outer");
        v.Set("acc", "kept");

        using (v.Scope("item"))
        {
            v.Set("item", "inner");
            v.Set("acc", "changed-inside");
            Assert.Equal("inner", v.Get("item"));
        }

        Assert.Equal("outer", v.Get("item"));           // 循环变量还原
        Assert.Equal("changed-inside", v.Get("acc"));  // 循环体攒下的结果留在外面
    }

    [Fact]
    public void Scope_of_a_name_that_did_not_exist_ends_with_it_gone()
    {
        var v = new RunVars();
        using (v.Scope("never-set")) v.Set("never-set", "temp");
        Assert.False(v.Has("never-set"));
    }

    [Fact]
    public void Scope_survives_an_exception_inside_the_block()
    {
        var v = new RunVars();
        v.Set("item", "outer");
        try { using (v.Scope("item")) { v.Set("item", "inner"); throw new InvalidOperationException(); } }
        catch (InvalidOperationException) { }
        Assert.Equal("outer", v.Get("item"));
    }

    // ---- 算术 ----

    [Theory]
    [InlineData("3+1", "4")]
    [InlineData("3 + 1", "4")]
    [InlineData("10-4", "6")]
    [InlineData("3*4", "12")]
    [InlineData("7/2", "3.5")]
    [InlineData("2.5*2", "5")]
    [InlineData("-3+1", "-2")]      // 负的左操作数不该被当成运算符
    public void Arithmetic_works_on_the_simple_shape(string expr, string want)
    {
        var r = StepMath.Evaluate(expr);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(want, r.Value);
    }

    [Fact]
    public void Dividing_by_zero_says_so_instead_of_producing_infinity()
    {
        var r = StepMath.Evaluate("5/0");
        Assert.False(r.Ok);
        Assert.Contains("0", r.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("3+")]
    [InlineData("3 + x")]
    public void A_malformed_expression_says_what_is_wrong(string expr)
        => Assert.False(StepMath.Evaluate(expr).Ok);

    // 「+1」**不是**写错了：整个串自己就是一个数时照原样给回去（见 StepMath 里那段）。
    // 前导 + 在数值记法里合法，用户写它不算错——报「没有找到运算符」反而是在指责一个能算的式子。
    [Fact]
    public void A_bare_number_passes_straight_through()
    {
        Assert.Equal("1", StepMath.Evaluate("+1").Value);
        Assert.Equal("42", StepMath.Evaluate(" 42 ").Value);
    }

    [Fact]
    public void A_whole_result_loses_its_trailing_zero()
    {
        // 循环里累加出来的数不该印成「4.000000」
        Assert.Equal("4", StepMath.Evaluate("2+2").Value);
        Assert.Equal("4.5", StepMath.Evaluate("2+2.5").Value);
    }

    // ---- 比较 ----

    [Theory]
    [InlineData("abc", "eq", "abc", true)]
    [InlineData("abc", "eq", "abd", false)]
    [InlineData("abc", "ne", "abd", true)]
    [InlineData("hello", "contains", "ell", true)]
    [InlineData("hello", "notcontains", "xyz", true)]
    [InlineData("", "empty", "", true)]
    [InlineData("x", "notempty", "", true)]
    public void Text_comparisons_work(string a, string op, string b, bool want)
        => Assert.Equal(want, StepMath.Compare(a, op, b));

    // 大小于先试数值：字符串比较下 "9" > "10" 是成立的，而用户要的是个数。
    [Fact]
    public void Greater_than_compares_as_numbers_when_both_sides_are_numbers()
    {
        Assert.True(StepMath.Compare("9", "gt", "10") == false);
        Assert.True(StepMath.Compare("11", "gt", "10"));
    }

    [Fact]
    public void Greater_than_falls_back_to_text_when_a_side_is_not_a_number()
    {
        Assert.True(StepMath.Compare("b", "gt", "a"));
        Assert.True(StepMath.Compare("apple", "lt", "banana"));
    }

    // ---- 键名表 ----

    [Theory]
    [InlineData("Insert", 0x2D)]
    [InlineData("insert", 0x2D)]
    [InlineData("Escape", 0x1B)]
    [InlineData("esc", 0x1B)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("A", 0x41)]
    [InlineData("Enter", 0x0D)]
    public void Key_names_map_to_their_virtual_key_codes(string name, int want)
        => Assert.Equal(want, KeyNames.ToVk(name));

    [Fact]
    public void An_unknown_key_name_maps_to_nothing_rather_than_guessing()
    {
        Assert.Null(KeyNames.ToVk("NotAKey"));
        Assert.Null(KeyNames.ToVk(""));
        Assert.Null(KeyNames.ToVk(null));
    }

    [Fact]
    public void A_comma_separated_key_list_parses_and_reports_the_bad_ones()
    {
        var (ok, bad) = KeyNames.ParseList("Insert, Escape ,F1");
        Assert.Equal(3, ok.Count);
        Assert.Empty(bad);

        var (ok2, bad2) = KeyNames.ParseList("Insert, Nope, AlsoNope");
        Assert.Single(ok2);
        Assert.Equal(new[] { "Nope", "AlsoNope" }, bad2);
    }

    [Fact]
    public void A_pressed_key_comes_back_as_a_name_a_user_can_type_into_a_condition()
        => Assert.Equal("Escape", KeyNames.ToName(0x1B));
}
