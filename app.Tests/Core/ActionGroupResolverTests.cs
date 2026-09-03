using System.Linq;
using System.Globalization;
using Clockwork.Core;
using Xunit;

public class ActionGroupResolverTests
{
    private static List<ActionGroup> Groups() => new()
    {
        new ActionGroup { Id = "a", Name = "组A" },
        new ActionGroup { Id = "b", Name = "组B" },
    };

    [Fact] public void Resolves_by_id() => Assert.Equal("组B", ActionGroupResolver.Resolve(Groups(), "b")!.Name);
    [Fact] public void Empty_id_null() => Assert.Null(ActionGroupResolver.Resolve(Groups(), ""));
    [Fact] public void Missing_id_null() => Assert.Null(ActionGroupResolver.Resolve(Groups(), "zzz"));
    [Fact] public void Null_list_null() => Assert.Null(ActionGroupResolver.Resolve(null, "a"));

    private static ActionGroup G(string id, string name, params string[] refIds)
        => new ActionGroup { Id = id, Name = name, Steps = refIds.Select(r => new LaunchStep { Kind = "group", GroupId = r }).ToList() };

    [Fact]
    public void FindCycle_none_returns_null()
        => Assert.Null(ActionGroupResolver.FindCycle(new[] { G("a", "A", "b"), G("b", "B") }, "a"));

    [Fact]
    public void FindCycle_direct_self_reference()
    {
        var cycle = ActionGroupResolver.FindCycle(new[] { G("a", "A", "a") }, "a");
        Assert.Equal(new[] { "A", "A" }, cycle);
    }

    [Fact]
    public void FindCycle_indirect_a_b_a()
    {
        var cycle = ActionGroupResolver.FindCycle(new[] { G("a", "A", "b"), G("b", "B", "a") }, "a");
        Assert.Equal(new[] { "A", "B", "A" }, cycle);
    }

    [Fact]
    public void FindCycle_diamond_is_not_a_cycle()
    {
        // A→B、A→C、B→D、C→D：同一组被两条路径引用是合法复用，不是环。
        var groups = new[] { G("a", "A", "b", "c"), G("b", "B", "d"), G("c", "C", "d"), G("d", "D") };
        Assert.Null(ActionGroupResolver.FindCycle(groups, "a"));
    }

    [Fact]
    public void FindCycle_dangling_reference_ignored()
        => Assert.Null(ActionGroupResolver.FindCycle(new[] { G("a", "A", "ghost") }, "a"));

    [Fact]
    public void FindCycle_foreign_cycle_not_reported()
    {
        // B↔C 互指，但从 A 出发到不了回 A 的环——那个环在保存 B/C 时自会被拦，不在 A 这里误报。
        var groups = new[] { G("a", "A", "b"), G("b", "B", "c"), G("c", "C", "b") };
        Assert.Null(ActionGroupResolver.FindCycle(groups, "a"));
    }

    // ResolveForRun：嵌套组引用（RunGroupStep）目标解析 + 分类。benign 决定 App.xaml.cs 里弹的是
    // 静默 Info 还是刺眼 Warn——这是本组测试要保护的关键位，不是顺带断言。

    [Fact]
    public void ResolveForRun_not_found_is_not_benign()
    {
        var target = ActionGroupResolver.ResolveForRun(Groups(), "zzz");
        Assert.Null(target.Group);
        Assert.NotNull(target.Skip);
        Assert.False(target.Skip!.Benign);
        Assert.False(string.IsNullOrEmpty(target.Skip.Text()));
    }

    [Fact]
    public void ResolveForRun_empty_id_is_not_found()
    {
        var target = ActionGroupResolver.ResolveForRun(Groups(), "");
        Assert.Null(target.Group);
        Assert.False(target.Skip!.Benign);
    }

    [Fact]
    public void ResolveForRun_whitespace_id_is_not_found()
    {
        var target = ActionGroupResolver.ResolveForRun(Groups(), "   ");
        Assert.Null(target.Group);
        Assert.False(target.Skip!.Benign);
    }

    [Fact]
    public void ResolveForRun_null_list_is_not_found()
    {
        var target = ActionGroupResolver.ResolveForRun(null, "a");
        Assert.Null(target.Group);
        Assert.False(target.Skip!.Benign);
    }

    [Fact]
    public void ResolveForRun_disabled_target_is_benign_and_names_the_group()
    {
        var groups = new List<ActionGroup> { new ActionGroup { Id = "a", Name = "组A", Enabled = false } };
        var target = ActionGroupResolver.ResolveForRun(groups, "a");
        Assert.Null(target.Group);
        Assert.NotNull(target.Skip);
        Assert.True(target.Skip!.Benign);
        Assert.Contains("组A", target.Skip.Text());   // Lf 用 {0} 填组名——这是用它的全部意义所在
    }

    [Fact]
    public void ResolveForRun_enabled_target_returns_same_instance()
    {
        var groups = Groups();
        var target = ActionGroupResolver.ResolveForRun(groups, "b");
        Assert.Null(target.Skip);
        Assert.Same(groups[1], target.Group);
    }

    [Fact]
    public void Reentrant_reason_is_not_benign()
    {
        var skip = ActionGroupResolver.Reentrant();
        Assert.False(skip.Benign);
        Assert.False(string.IsNullOrEmpty(skip.Text()));
    }

    // GroupSkip 存的是键而不是渲染好的句子，为的就是同一个原因能出两种语言：
    // 气泡跟界面语言、clockwork.error.log 固定英文。这两条盯的是「英文那一份真的是英文」
    // 和「渲染完文化被还原」——后者错了会静默把整个界面切成英文。
    [Fact]
    public void TextEn_is_english_even_when_the_ui_is_chinese()
    {
        var save = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var skip = ActionGroupResolver.Reentrant();
            var zh = skip.Text();
            var en = skip.TextEn();
            Assert.NotEqual(zh, en);
            Assert.Contains("重入", zh);            // 界面那一份跟 UI 文化，此处是中文
            Assert.DoesNotContain("重入", en);      // 英文那一份不该带中文
            Assert.StartsWith("Action re-entered", en);
        }
        finally { CultureInfo.CurrentUICulture = save; }
    }

    [Fact]
    public void TextEn_restores_the_ui_culture()
    {
        var save = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            ActionGroupResolver.Reentrant().TextEn();
            Assert.Equal("zh-CN", CultureInfo.CurrentUICulture.Name);
        }
        finally { CultureInfo.CurrentUICulture = save; }
    }

    [Fact]
    public void NotFound_and_disabled_reasons_differ()
    {
        // 找不到 vs 已禁用 走不同 resx key；若两处误用同一 key，这条测试能抓到（配合上面两条各自的 Benign 断言）。
        var groups = new List<ActionGroup> { new ActionGroup { Id = "a", Name = "组A", Enabled = false } };
        var notFound = ActionGroupResolver.ResolveForRun(groups, "zzz");
        var disabled = ActionGroupResolver.ResolveForRun(groups, "a");
        Assert.NotEqual(notFound.Skip!.Text(), disabled.Skip!.Text());
    }
}
