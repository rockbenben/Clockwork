using System.Linq;
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
}
