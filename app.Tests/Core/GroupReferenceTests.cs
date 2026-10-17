using System.Linq;
using Clockwork.Core;
using Xunit;

// 「引用一个已有动作组」这条路上的两类**静默数据损坏**。
// 两类都不会报错、界面上也看不出来，只有事后发现「我的步骤指错了组」「组名怎么还是旧的」。
public class GroupReferenceTests
{
    private static RootConfig Cfg()
    {
        var c = new RootConfig();
        c.ActionGroups.Add(new ActionGroup { Id = "g1", Name = "离开一下" });
        c.ActionGroups.Add(new ActionGroup { Id = "g2", Name = "收工" });
        return c;
    }

    // 组改名后，引用它的步骤 Label 必须跟上。Label 是组名的缓存副本（只在编辑那一步写入），
    // 不同步的话启动清单会一直显示旧名——而那正是用户用来认出这一步的名字。
    [Fact]
    public void Renaming_a_group_updates_every_reference()
    {
        var c = Cfg();
        c.LaunchSteps.Add(new LaunchStep { Kind = "group", GroupId = "g1", Label = "离开一下" });
        c.Gestures.Add(new LaunchStep { Kind = "group", GroupId = "g1", Label = "离开一下", Gesture = "RD" });
        c.ActionGroups[1].Steps.Add(new LaunchStep { Kind = "group", GroupId = "g1", Label = "离开一下" });

        c.ActionGroups[0].Name = "午休";
        Assert.True(ConfigStore.Normalize(c));

        Assert.Equal("午休", c.LaunchSteps[0].Label);
        Assert.Equal("午休", c.Gestures[0].Label);                 // 手势那一份同样要跟上
        Assert.Equal("午休", c.ActionGroups[1].Steps[0].Label);    // 组里引用组的也要
    }

    // 目标组已被删除时，Label 是仅存的线索，不能抹掉——抹了就再也说不出这一步原本指向谁。
    [Fact]
    public void Dangling_reference_keeps_its_last_known_name()
    {
        var c = Cfg();
        c.LaunchSteps.Add(new LaunchStep { Kind = "group", GroupId = "gone", Label = "早就删了的组" });
        ConfigStore.Normalize(c);
        Assert.Equal("早就删了的组", c.LaunchSteps[0].Label);
        Assert.Equal("gone", c.LaunchSteps[0].GroupId);
    }

    // 同步必须幂等：名字已经对齐时不能再报「配置变了」，否则每次读盘都会白写一次盘。
    [Fact]
    public void Sync_is_idempotent()
    {
        var c = Cfg();
        c.LaunchSteps.Add(new LaunchStep { Kind = "group", GroupId = "g1", Label = "离开一下" });
        ConfigStore.Normalize(c);
        Assert.False(StepDisplay.SyncGroupStepLabels(c.LaunchSteps, c.ActionGroups));
    }

    // 只碰 group 步骤：别的类型 Label 是用户自己填的，动它就是改用户的数据。
    [Fact]
    public void Only_group_steps_are_touched()
    {
        var c = Cfg();
        var app = new LaunchStep { Kind = "app", GroupId = "g1", Label = "我自己起的名字" };
        c.LaunchSteps.Add(app);
        ConfigStore.Normalize(c);
        Assert.Equal("我自己起的名字", app.Label);
    }

    // 「动作组」必须自成一节：它原来埋在「流程」第三项，想找的人翻不到。
    // **不再钉「排在最前」**：分节改成照 Quicker 的模块分类排之后，最前面那一格是「常用」
    // （打开网址 / 运行程序 / 发送文本那一批日常原子操作）。「动作组」是复用而不是日常动作，
    // 排到末位反而更符合它在那张表里的位置——而它只有一节，工具箱与菜单里都一眼找得到。
    [Fact]
    public void Group_kind_is_its_own_section_and_appears_nowhere_else()
    {
        var withGroup = StepDisplay.StepKindSections.Where(s => s.Kinds.Contains("group")).ToList();
        Assert.Single(withGroup);
        Assert.Equal("Tab_Group", withGroup[0].SectionKey);
        // 只归一节，且那一节里只有它（别把别的东西塞进「动作组」里）
        Assert.Equal(new[] { "group" }, withGroup[0].Kinds);
    }

    // 每种类型都还得从菜单/工具箱里够得着：分节是索引，「至少出现一次」是底线。
    [Fact]
    public void Every_kind_is_still_reachable_from_the_menu()
    {
        var inMenu = StepDisplay.StepKindSections.SelectMany(s => s.Kinds).Distinct();
        Assert.Equal(StepDisplay.StepKinds.OrderBy(x => x), inMenu.OrderBy(x => x));
    }
}
