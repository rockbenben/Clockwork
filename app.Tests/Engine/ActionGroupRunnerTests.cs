using System.Linq;
using Clockwork.Engine;
using Clockwork.Core;
using Xunit;

public class ActionGroupRunnerTests
{
    public ActionGroupRunnerTests() => StopSignal.Clear();

    private static GroupDeps Deps(List<string> ran, Func<LaunchStep, MsgResult>? msg = null, List<string>? onYes = null)
        => new GroupDeps
        {
            Hour = 10,
            IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            ShowMessage = msg ?? (_ => MsgResult.Ok),
            RunOnYes = s => (onYes ?? new()).Add(s.Label),
            Speak = _ => { },
        };

    [Fact]
    public void Runs_all_non_message_steps()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "g1", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "1" }, new LaunchStep { Kind = "keys", Combo = "a", Label = "2" } } };
        ActionGroupRunner.RunGroup(g, Deps(ran));
        Assert.Equal(new[] { "1", "2" }, ran.ToArray());
    }

    [Fact]
    public void Message_no_aborts_group()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "g2", Steps = new() { new LaunchStep { Kind = "message", Message = "q", Label = "m" }, new LaunchStep { Kind = "volume", Action = "mute", Label = "after" } } };
        ActionGroupRunner.RunGroup(g, Deps(ran, msg: _ => MsgResult.No));
        Assert.Empty(ran);   // No → 中止，after 不跑
    }

    [Fact]
    public void Message_yes_runs_onYes_and_continues()
    {
        var ran = new List<string>();
        var yes = new List<string>();
        var g = new ActionGroup { Id = "g3", Steps = new() { new LaunchStep { Kind = "message", Message = "q", Label = "m" }, new LaunchStep { Kind = "volume", Action = "mute", Label = "after" } } };
        ActionGroupRunner.RunGroup(g, Deps(ran, msg: _ => MsgResult.Yes, onYes: yes));
        Assert.Equal(new[] { "m" }, yes.ToArray());
        Assert.Equal(new[] { "after" }, ran.ToArray());
    }

    [Fact]
    public void Disabled_and_condition_skipped()
    {
        var ran = new List<string>();
        var g = new ActionGroup
        {
            Id = "g4",
            Steps = new()
            {
                new LaunchStep { Kind = "volume", Action = "mute", Label = "disabled", Enabled = false },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "wrongday", Days = new() { 6 } },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "ok" },
            }
        };
        ActionGroupRunner.RunGroup(g, Deps(ran));   // Hour=10, IsoDay=3(周三)
        Assert.Equal(new[] { "ok" }, ran.ToArray());
    }

    [Fact]
    public void Loop_repeat_runs_step_n_times()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "g5", Steps = new() { new LaunchStep { Kind = "keys", Combo = "a", Label = "x", Repeat = 3 } } };
        ActionGroupRunner.RunGroup(g, Deps(ran));
        Assert.Equal(3, ran.Count);
    }

    [Fact]
    public void Throwing_step_is_contained_group_continues()
    {
        // 某步抛异常（如剪贴板被占用）不得中止整组——收工/睡前组里锁屏必须照跑。
        var ran = new List<string>();
        var errors = new List<string>();
        var deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => { if (s.Label == "boom") throw new InvalidOperationException("clipboard busy"); ran.Add(s.Label); },
            OnStepError = (s, _) => errors.Add(s.Label),
        };
        var g = new ActionGroup
        {
            Id = "gerr",
            Steps = new()
            {
                new LaunchStep { Kind = "system", Command = "clearClipboard", Label = "boom" },
                new LaunchStep { Kind = "system", Command = "lockScreen", Label = "after" },
            }
        };
        ActionGroupRunner.RunGroup(g, deps);
        Assert.Equal(new[] { "after" }, ran.ToArray());   // 抛异常步骤后面的步骤仍执行
        Assert.Equal(new[] { "boom" }, errors.ToArray());  // 失败被上报（不静默）
    }

    [Fact]
    public void Group_level_repeat_runs_rounds()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "gr1", Repeat = 3, Steps = new() { new LaunchStep { Kind = "keys", Combo = "a", Label = "x" } } };
        Assert.Equal(GroupRunResult.Completed, ActionGroupRunner.RunGroup(g, Deps(ran)));
        Assert.Equal(3, ran.Count);   // 整组 3 轮 × 每轮 1 步
    }

    [Fact]
    public void Group_repeat_multiplies_with_step_repeat()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "gr2", Repeat = 2, Steps = new() { new LaunchStep { Kind = "keys", Combo = "a", Label = "x", Repeat = 3 } } };
        ActionGroupRunner.RunGroup(g, Deps(ran));
        Assert.Equal(6, ran.Count);   // 2 轮 × 单步 3 次
    }

    [Fact]
    public void Reentry_returns_skipped()
    {
        // 同 id 已在跑 → 第二次进入直接 Skipped（环重入的运行期兜底；上报由调用方决定）。
        var g = new ActionGroup { Id = "gr3", Steps = new() { new LaunchStep { Kind = "keys", Combo = "a", Label = "x" } } };
        var inner = GroupRunResult.Completed;
        var deps = new GroupDeps { Hour = 10, IsoDay = 3, RunStep = _ => inner = ActionGroupRunner.RunGroup(g, Deps(new List<string>())) };
        Assert.Equal(GroupRunResult.Completed, ActionGroupRunner.RunGroup(g, deps));
        Assert.Equal(GroupRunResult.Skipped, inner);
    }

    [Fact]
    public void Budget_exhaustion_stops_and_fires_once()
    {
        var ran = new List<string>();
        int warned = 0;
        var deps = new GroupDeps { Hour = 10, IsoDay = 3, RunStep = s => ran.Add(s.Label), Budget = new RunBudget(() => warned++) };
        // 999 × 6 = 5994 > 5000：预算耗尽即停，回调只响一次。DelayMs=0 必写——默认 100 会让这条测试真睡 500 秒。
        var g = new ActionGroup { Id = "gr4", Steps = Enumerable.Range(0, 6).Select(_ => new LaunchStep { Kind = "keys", Combo = "a", Label = "x", Repeat = 999, DelayMs = 0 }).ToList() };
        ActionGroupRunner.RunGroup(g, deps);
        Assert.Equal(RunBudget.MaxRunSteps, ran.Count);
        Assert.Equal(1, warned);
        Assert.True(deps.Budget.Exhausted);
    }

    [Fact]
    public void Message_no_aborts_remaining_rounds()
    {
        // 「否」停的是整组、含后续轮次，不只是当前这一轮：整组 Repeat=3 时答一次否就结束，
        // 否则同一个确认框会被重弹 3 次。挡「stopped 只 break 当前轮」的实现。
        var ran = new List<string>();
        int asked = 0;
        var g = new ActionGroup
        {
            Id = "gno1", Repeat = 3, RepeatDelayMs = 0,
            Steps = new()
            {
                new LaunchStep { Kind = "message", Message = "存盘了吗", Label = "m", DelayMs = 0 },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "after", DelayMs = 0 },
            }
        };
        var res = ActionGroupRunner.RunGroup(g, Deps(ran, msg: _ => { asked++; return MsgResult.No; }));
        Assert.Equal(GroupRunResult.Aborted, res);   // 结局必须可区分：父级要靠它收手
        Assert.Equal(1, asked);                      // 只问一次
        Assert.Empty(ran);
    }

    [Fact]
    public void Message_no_in_child_stops_parent_reference_iterations()
    {
        // 「循环一段步骤」的推荐做法（抽成子组 + 引用 ×N）恰好穿过这个边界：子组里答一次「否」，
        // 父组的引用轮次必须立刻收手并把中止继续往上传。挡「RunGroup 恒返回成功」的实现——
        // 那种实现下同一个模态确认框会被重弹 N 次。
        var ran = new List<string>();
        int asked = 0, childRuns = 0;
        var child = new ActionGroup
        {
            Id = "child",
            Steps = new()
            {
                new LaunchStep { Kind = "message", Message = "继续？", Label = "m", DelayMs = 0 },
                new LaunchStep { Kind = "keys", Combo = "a", Label = "childstep", DelayMs = 0 },
            }
        };
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            ShowMessage = _ => { asked++; return MsgResult.No; },
            RunGroupStep = _ => { childRuns++; return ActionGroupRunner.RunGroup(child, deps); },
        };
        var parent = new ActionGroup
        {
            Id = "parent",
            Steps = new()
            {
                new LaunchStep { Kind = "group", GroupId = "child", Label = "ref", Repeat = 3, DelayMs = 0 },
                new LaunchStep { Kind = "keys", Combo = "b", Label = "afterref", DelayMs = 0 },
            }
        };
        Assert.Equal(GroupRunResult.Aborted, ActionGroupRunner.RunGroup(parent, deps));
        Assert.Equal(1, childRuns);   // 子组只跑一次，父级不再重新引用
        Assert.Equal(1, asked);
        Assert.Empty(ran);            // 引用步骤之后的步骤也一并停
    }

    [Fact]
    public void Reentrant_reference_reports_via_OnStepSkipped_not_OnStepError_and_stops_iterating()
    {
        // 重入注定持续整个循环（同一 id 还在调用栈上）：Repeat=999 的自引用以前会上报 999 次 + 空睡 999 次。
        // 与生产代码（App.xaml.cs BuildGroupDeps 的 RunGroupStep）同款接线：重入不是异常，经 OnStepSkipped
        // 上报（benign=false——这是真问题，不是正常配置状态），OnStepError 保留给 RunStep 真正抛出的异常。
        var skipped = new List<(string Label, string Reason, bool Benign)>();
        var errors = new List<string>();
        int calls = 0;
        var g = new ActionGroup { Id = "self", Steps = new() { new LaunchStep { Kind = "group", GroupId = "self", Label = "ref", Repeat = 999, DelayMs = 0 } } };
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            OnStepError = (s, _) => errors.Add(s.Label),
            OnStepSkipped = (s, reason, benign) => skipped.Add((s.Label, reason, benign)),
            RunGroupStep = s =>
            {
                calls++;
                var r = ActionGroupRunner.RunGroup(g, deps);
                if (r == GroupRunResult.Skipped) deps.OnStepSkipped(s, "动作组重入（环引用或已在运行），已跳过", false);
                return r;
            },
        };
        ActionGroupRunner.RunGroup(g, deps);
        Assert.Equal(1, calls);    // 第一次就知道结论，不再迭代（ActionGroupRunner 收到 Skipped 就 break，行为不能被这次改动动到）
        Assert.Empty(errors);      // 不是异常，不该走 OnStepError
        var one = Assert.Single(skipped);
        Assert.Equal("ref", one.Label);
        Assert.False(string.IsNullOrEmpty(one.Reason));   // 原因文本非空——用户能看懂为什么被跳过
        Assert.False(one.Benign);   // 重入是真问题，不是良性状态
    }

    [Fact]
    public void Benign_skip_still_stops_reference_iterations_but_lets_group_continue()
    {
        // 「目标组已禁用」（benign=true，与 App.xaml.cs BuildGroupDeps 的 RunGroupStep 同款接线：
        // 正常配置状态，不是故障）与「重入」共用同一条 Skipped 通路：ActionGroupRunner 只认 Skipped
        // 本身，不关心 benign 与否——收到就 break 掉这一步的 rep 循环，但不中止整组（这与「否」中止
        // 整组不同，是本次改动前后都成立的既有行为，这里锁定它对 benign 分支同样适用）。
        var skipped = new List<(string Reason, bool Benign)>();
        var errors = new List<string>();
        var ran = new List<string>();
        int calls = 0;
        var disabledTarget = new ActionGroup { Id = "disabledTarget", Enabled = false, Steps = new() };
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            OnStepError = (s, _) => errors.Add(s.Label),
            OnStepSkipped = (s, reason, benign) => skipped.Add((reason, benign)),
            RunGroupStep = s =>
            {
                calls++;
                deps.OnStepSkipped(s, $"动作组「{disabledTarget.Id}」已禁用，跳过", true);
                return GroupRunResult.Skipped;
            },
        };
        var parent = new ActionGroup
        {
            Id = "parent2",
            Steps = new()
            {
                new LaunchStep { Kind = "group", GroupId = "disabledTarget", Label = "ref", Repeat = 5, DelayMs = 0 },
                new LaunchStep { Kind = "keys", Combo = "b", Label = "afterref", DelayMs = 0 },
            }
        };
        var result = ActionGroupRunner.RunGroup(parent, deps);
        Assert.Equal(GroupRunResult.Completed, result);   // benign 跳过不中止整组（不同于「否」）
        Assert.Equal(1, calls);                            // Repeat=5 但只问一次结论——同一目标状态不变，不重复空转
        Assert.Empty(errors);
        var one = Assert.Single(skipped);
        Assert.False(string.IsNullOrEmpty(one.Reason));
        Assert.True(one.Benign);
        Assert.Equal(new[] { "afterref" }, ran.ToArray());  // 跳过之后的步骤照常执行
    }

    [Fact]
    public void Budget_charges_group_reference_iterations()
    {
        // 纯引用链（叶子组不含普通步骤）以前一步预算都不计：999×999 的展开能完全绕过 5000 步保险丝，
        // 而「单次运行最多 5000 步」是文档写给用户的承诺。每次引用迭代计一步 → 必须截停。
        // DelayMs=0 必写——默认 100 会让这条测试真睡到超时。
        int warned = 0;
        int calls = 0;
        var leaf = new ActionGroup { Id = "leaf", Steps = new() };   // 空叶子：老实现下永不计费
        var mid = new ActionGroup { Id = "mid", Steps = new() { new LaunchStep { Kind = "group", GroupId = "leaf", Label = "toLeaf", Repeat = 999, DelayMs = 0 } } };
        var top = new ActionGroup { Id = "top", Steps = new() { new LaunchStep { Kind = "group", GroupId = "mid", Label = "toMid", Repeat = 999, DelayMs = 0 } } };
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            Budget = new RunBudget(() => warned++),
            RunGroupStep = s => { calls++; return ActionGroupRunner.RunGroup(s.GroupId == "mid" ? mid : leaf, deps); },
        };
        ActionGroupRunner.RunGroup(top, deps);
        Assert.True(deps.Budget.Exhausted);
        Assert.Equal(1, warned);
        Assert.Equal(RunBudget.MaxRunSteps, calls);   // 每次迭代恰好一步预算，用尽即止
    }
}
