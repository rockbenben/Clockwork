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

    // 「动作组」必须自成一节且排在最前：它原来埋在「流程」第三项，想找的人翻不到。
    [Fact]
    public void Group_kind_is_its_own_first_menu_section()
    {
        var first = StepDisplay.StepKindSections[0];
        Assert.Equal("Tab_Group", first.SectionKey);
        Assert.Equal(new[] { "group" }, first.Kinds);
        Assert.DoesNotContain("group", StepDisplay.StepKindSections.Skip(1).SelectMany(s => s.Kinds));
    }

    // 十种类型一个不少——分节重排最容易把某一种漏在外面，那种漏法界面上只表现为「菜单里少一项」。
    [Fact]
    public void Every_kind_is_still_reachable_from_the_menu()
    {
        var inMenu = StepDisplay.StepKindSections.SelectMany(s => s.Kinds).OrderBy(x => x);
        Assert.Equal(StepDisplay.StepKinds.OrderBy(x => x), inMenu);
    }
}
