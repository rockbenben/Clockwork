using System.Collections.Generic;
using System.Linq;
using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// 问句步骤在动作里的编排：问来的值进变量表，取消中止整组。
// 弹框本身测不了（那要 UI 线程和一个真人），所以经 GroupDeps.AskUser 这个 seam 注入答案——
// 与 message 步骤的测法一样，验的是**编排**，不是那扇窗。
public class AskFlowTests
{
    public AskFlowTests() => StopSignal.Clear();

    private static GroupDeps Deps(List<string> ran, RunVars vars, Func<LaunchStep, string?> ask)
        => new GroupDeps
        {
            Hour = 10,
            IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            AskUser = ask,
            Vars = vars,
        };

    private static ActionGroup Group(params LaunchStep[] steps)
        => new() { Id = System.Guid.NewGuid().ToString(), Steps = steps.ToList() };

    // 答完就进变量表，后面的步骤能引用到——这是整套东西存在的理由。
    [Fact]
    public void An_answer_lands_in_the_variable()
    {
        var ran = new List<string>();
        var vars = new RunVars();
        var g = Group(
            new LaunchStep { Kind = "prompt", Message = "搜什么？", OutputVar = "关键词", Label = "问" },
            new LaunchStep { Kind = "url", Target = "https://x/?q={关键词}", Label = "开" });
        ActionGroupRunner.RunGroup(g, Deps(ran, vars, _ => "猫"));
        Assert.Equal("猫", vars.Get("关键词"));
        Assert.Equal(new[] { "开" }, ran.ToArray());   // 问完接着往下跑
    }

    // **取消 = 这组别做了**，与 message 答「否」同一个语义：
    // 用户按 Esc 的意思不是「给个空答案接着跑」，而后面的步骤多半正等着那个值。
    [Fact]
    public void Cancelling_aborts_the_rest_of_the_group()
    {
        var ran = new List<string>();
        var vars = new RunVars();
        var g = Group(
            new LaunchStep { Kind = "prompt", Message = "搜什么？", OutputVar = "关键词", Label = "问" },
            new LaunchStep { Kind = "volume", Action = "mute", Label = "后面" });
        var r = ActionGroupRunner.RunGroup(g, Deps(ran, vars, _ => null));
        Assert.Empty(ran);
        Assert.Equal(GroupRunResult.Aborted, r);
        Assert.Null(vars.Get("关键词"));   // 取消的答案连记都不该记
    }

    // 空答案不是取消：用户就是想传一个空串（「备注」留空照样往下走）。
    // 这两件事混在一起的话，回车放空会把整组停掉，而那读起来完全像是「程序卡住了」。
    [Fact]
    public void An_empty_answer_is_not_a_cancel()
    {
        var ran = new List<string>();
        var vars = new RunVars();
        var g = Group(
            new LaunchStep { Kind = "prompt", Message = "备注？", OutputVar = "备注", Label = "问" },
            new LaunchStep { Kind = "volume", Action = "mute", Label = "后面" });
        ActionGroupRunner.RunGroup(g, Deps(ran, vars, _ => ""));
        Assert.Equal(new[] { "后面" }, ran.ToArray());
        Assert.Equal("", vars.Get("备注"));
    }

    // 没配输出变量照样能问（「问一句确认一下」也是用法），只是没有东西被记下来。
    [Fact]
    public void An_ask_without_an_output_variable_still_runs()
    {
        var ran = new List<string>();
        var vars = new RunVars();
        var g = Group(
            new LaunchStep { Kind = "choice", Message = "去哪儿？", Text = "上班\n下班", Label = "选" },
            new LaunchStep { Kind = "volume", Action = "mute", Label = "后面" });
        ActionGroupRunner.RunGroup(g, Deps(ran, vars, _ => "上班"));
        Assert.Equal(new[] { "后面" }, ran.ToArray());
        Assert.Empty(vars.Names);
    }

    // 后问的覆盖先问的：同一个变量名问两次，用的是最后那个答案。
    [Fact]
    public void Asking_twice_into_one_name_keeps_the_last_answer()
    {
        var ran = new List<string>();
        var vars = new RunVars();
        int n = 0;
        var g = Group(
            new LaunchStep { Kind = "prompt", Message = "一？", OutputVar = "w", Label = "a" },
            new LaunchStep { Kind = "prompt", Message = "二？", OutputVar = "w", Label = "b" });
        ActionGroupRunner.RunGroup(g, Deps(ran, vars, _ => (++n).ToString()));
        Assert.Equal("2", vars.Get("w"));
    }

    // **子动作问来的值，父动作后面的步骤要引用得到**——那才叫「一次运行里的变量」。
    // 整条嵌套链共用一份表（同 Budget / Cancel），这条测试盯着那个共用不被改掉。
    [Fact]
    public void A_nested_group_shares_the_same_variables()
    {
        var ran = new List<string>();
        var vars = new RunVars();
        var child = Group(new LaunchStep { Kind = "prompt", Message = "？", OutputVar = "从子组来的", Label = "问" });
        var parent = Group(new LaunchStep { Kind = "group", GroupId = child.Id, Label = "跑子组" });
        var deps = new GroupDeps
        {
            Hour = 10,
            IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            AskUser = _ => "值",
            Vars = vars,
        };
        // 嵌套调用共享同一个 deps，也就是同一份变量表——这正是要盯住的那条约定。
        var withNesting = new GroupDeps
        {
            Hour = deps.Hour,
            IsoDay = deps.IsoDay,
            RunStep = deps.RunStep,
            AskUser = deps.AskUser,
            Vars = deps.Vars,
            RunGroupStep = _ => ActionGroupRunner.RunGroup(child, deps),
        };
        ActionGroupRunner.RunGroup(parent, withNesting);
        Assert.Equal("值", vars.Get("从子组来的"));
    }
}
