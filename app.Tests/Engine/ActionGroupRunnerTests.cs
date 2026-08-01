using System.Diagnostics;
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

    // —— 单组取消（动作组热键的第二次按键）——
    // 语义边界：取消只停「这一次运行」，不碰全局急停、不碰别的组；跑完即腾位，下一次触发是全新的一次。
    //
    // 「谁在跑」(_running) 与「按哪个键能取消谁」(_topRuns) 是两张表：只有顶层运行进后者，
    // 所以测试必须像 App.RunGroupAsync 那样显式登记顶层，直接调 RunGroup 得到的是嵌套子组的处境。
    private static GroupRunResult RunTopLevel(ActionGroup g, GroupDeps deps)
    {
        bool owned = ActionGroupRunner.EnterTopLevel(g.Id, deps.Cancel);
        try { return ActionGroupRunner.RunGroup(g, deps); }
        finally { if (owned) ActionGroupRunner.ExitTopLevel(g.Id, deps.Cancel); }
    }

    [Fact]
    public void RequestCancel_returns_false_when_nothing_is_running()
        => Assert.False(ActionGroupRunner.RequestCancel("no-such-group"));

    // 开机清单内联展开会占住同一个运行集：此时任何来源再触发同一组都该判重入跳过，
    // 而不是并发跑第二份（关窗口/发按键/锁屏各来两轮）。
    [Fact]
    public void A_group_held_by_the_startup_list_is_not_run_again()
    {
        Assert.True(ActionGroupRunner.TryEnterRunning("boot1"));
        try
        {
            Assert.True(ActionGroupRunner.IsRunning("boot1"));
            var ran = new List<string>();
            var g = new ActionGroup { Id = "boot1", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "x", DelayMs = 0 } } };
            var res = ActionGroupRunner.RunGroup(g, new GroupDeps { Hour = 10, IsoDay = 3, RunStep = s => ran.Add(s.Label) });
            Assert.Equal(GroupRunResult.Skipped, res);
            Assert.Empty(ran);
            Assert.False(ActionGroupRunner.RequestCancel("boot1"));   // 在跑，但不是热键管得着的那一种
        }
        finally { ActionGroupRunner.ExitRunning("boot1"); }
        Assert.False(ActionGroupRunner.IsRunning("boot1"));
    }

    [Fact]
    public void Cancel_stops_remaining_steps_and_leaves_global_stop_clear()
    {
        var ran = new List<string>();
        var g = new ActionGroup
        {
            Id = "gc1",
            Steps = new()
            {
                new LaunchStep { Kind = "volume", Action = "mute", Label = "1", DelayMs = 0 },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "2", DelayMs = 0 },
            }
        };
        bool accepted = false;
        var deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => { ran.Add(s.Label); if (s.Label == "1") accepted = ActionGroupRunner.RequestCancel("gc1"); },
        };
        RunTopLevel(g, deps);
        Assert.True(accepted);                        // 有一份在跑 → 取消被受理（热键据此判「这次按键是取消不是启动」）
        Assert.Equal(new[] { "1" }, ran.ToArray());   // 第 2 步不再跑
        Assert.False(StopSignal.IsRequested);         // 只停这一次：全局急停没被顺手拉下（启动清单/别的组不受影响）
    }

    [Fact]
    public void Cancelling_a_nested_child_id_does_not_touch_the_parent_run()
    {
        // 按子组热键的本意是「跑一下这个子组」，不是「掐掉正拿它当步骤用的那个父组」。
        // 曾经运行集与取消表是同一张表，链上任一子组 id 都指向顶层的闸——于是这一按会把父组整轮杀掉
        // （父组的锁屏/息屏尾步全不执行），气泡还只报子组名，根本指不到真正被杀的那一个。
        // 现在子组只进「谁在跑」，不进「按哪个键能取消谁」：这一按取消不到任何东西，父组照常跑完。
        var ran = new List<string>();
        bool cancelAccepted = true;
        bool childSeenRunning = false;
        var child = new ActionGroup { Id = "child2", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "c1", DelayMs = 0 } } };
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s =>
            {
                ran.Add(s.Label);
                if (s.Label != "c1") return;
                cancelAccepted = ActionGroupRunner.RequestCancel("child2");
                childSeenRunning = ActionGroupRunner.IsRunning("child2");
            },
            RunGroupStep = _ => ActionGroupRunner.RunGroup(child, deps),
        };
        var parent = new ActionGroup
        {
            Id = "parent3",
            Steps = new()
            {
                new LaunchStep { Kind = "group", GroupId = "child2", Label = "ref", DelayMs = 0 },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "after", DelayMs = 0 },
            }
        };
        RunTopLevel(parent, deps);
        Assert.False(cancelAccepted);                          // 子组不是「可被热键取消的顶层运行」
        Assert.True(childSeenRunning);                         // 但它确实在跑——热键据此提示「在跑但这个键停不了」
        Assert.Equal(new[] { "c1", "after" }, ran.ToArray());  // 父组的尾步照常执行，没被误杀
    }

    [Fact]
    public void Cancelling_the_top_level_id_still_stops_the_whole_chain()
    {
        // 反向：取消顶层运行，嵌套子组与父组共用同一个闸，整条链一起收手。
        var ran = new List<string>();
        var child = new ActionGroup { Id = "child3", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "c1", DelayMs = 0 } } };
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => { ran.Add(s.Label); if (s.Label == "c1") Assert.True(ActionGroupRunner.RequestCancel("parent4")); },
            RunGroupStep = _ => ActionGroupRunner.RunGroup(child, deps),
        };
        var parent = new ActionGroup
        {
            Id = "parent4",
            Steps = new()
            {
                new LaunchStep { Kind = "group", GroupId = "child3", Label = "ref", DelayMs = 0 },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "after", DelayMs = 0 },
            }
        };
        RunTopLevel(parent, deps);
        Assert.Equal(new[] { "c1" }, ran.ToArray());   // 父组尾步不再跑
    }

    [Fact]
    public void Cancel_while_a_message_is_up_drops_the_answer_and_stops()
    {
        // 组卡在模态确认框上时按下取消：热键只能置位，弹窗要等用户点掉才返回。等它返回后，
        // 那个「是」不能再触发 onYes——按取消的意思是「这组别做了」，不是「把这一步做完再停」。
        var ran = new List<string>();
        var yes = new List<string>();
        var g = new ActionGroup
        {
            Id = "gc4",
            Steps = new()
            {
                new LaunchStep { Kind = "message", Message = "q", Label = "m", Confirm = true, DelayMs = 0 },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "after", DelayMs = 0 },
            }
        };
        var deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => ran.Add(s.Label),
            ShowMessage = _ => { ActionGroupRunner.RequestCancel("gc4"); return MsgResult.Yes; },
            RunOnYes = s => yes.Add(s.Label),
        };
        RunTopLevel(g, deps);
        Assert.Empty(yes);
        Assert.Empty(ran);
    }

    [Fact]
    public async Task Cancel_during_the_round_delay_wakes_the_run_up()
    {
        // 轮间延迟可以是几十分钟。只在进入睡眠前查一次取消，等于「按了取消要等这一觉睡完」——
        // 用户看到的和没取消没有区别。必须与取消信号一起等。
        var ran = new List<string>();
        var g = new ActionGroup
        {
            Id = "gc5", Repeat = 3, RepeatDelayMs = 10_000,
            Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "x", DelayMs = 0 } }
        };
        var deps = new GroupDeps { Hour = 10, IsoDay = 3, RunStep = s => ran.Add(s.Label) };
        // 不用固定 sleep 赌「这时候运行已登记」：CI 上一次抢占就会让 RequestCancel 落空、返回值又被丢弃，
        // 于是三轮跑满、20 秒后以「集合有 3 个元素」失败——真的丢取消和调度抖动看不出区别。
        // 改成轮询到受理为止，并把受理与否单独断言，失败信息直接指向原因。
        bool accepted = false;
        var t = Task.Run(() =>
        {
            var w = Stopwatch.StartNew();
            while (!(accepted = ActionGroupRunner.RequestCancel("gc5")) && w.ElapsedMilliseconds < 5000) Thread.Sleep(10);
        });
        var sw = Stopwatch.StartNew();
        RunTopLevel(g, deps);
        await t;
        Assert.True(accepted, "取消请求始终没被受理——本例的时序前提没成立，不是取消逻辑的问题");
        Assert.Single(ran);   // 第 1 轮跑完，睡到一半被取消 → 第 2 轮不再开
        Assert.True(sw.ElapsedMilliseconds < 5000, $"轮间延迟没被取消打断，耗时 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void A_cancelled_run_does_not_poison_the_next_one()
    {
        // 取消闸是「一次运行一份」。若做成组上的持久标志，取消过一次之后热键再按会当场自杀，
        // 而且没有任何反馈——那正是这次改动要根治的那种「按了没反应」。
        var g = new ActionGroup { Id = "gc6", Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "x", DelayMs = 0 } } };
        var first = new List<string>();
        RunTopLevel(g, new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => { first.Add(s.Label); ActionGroupRunner.RequestCancel("gc6"); },
        });
        var second = new List<string>();
        RunTopLevel(g, new GroupDeps { Hour = 10, IsoDay = 3, RunStep = s => second.Add(s.Label) });
        Assert.Single(first);
        Assert.Single(second);
        Assert.False(ActionGroupRunner.RequestCancel("gc6"));   // 跑完即腾位：不再有可取消的运行
    }

    // 急停必须能立刻叫醒睡在轮间/步间延迟里的在途组。RunCancel 只等自己那一个事件（不去等全局信号的
    // 内核句柄——那会引入两份可能永久分歧的状态），所以这条通路靠 CancelAll 主动推送。
    // 若哪天有人把 App.RequestStop 里的 CancelAll 删掉，急停对长睡眠的组就要等睡满才生效，本例会挂。
    [Fact]
    public async Task CancelAll_wakes_every_in_flight_run()
    {
        var ran = new List<string>();
        var g = new ActionGroup
        {
            Id = "gc8", Repeat = 3, RepeatDelayMs = 10_000,
            Steps = new() { new LaunchStep { Kind = "volume", Action = "mute", Label = "x", DelayMs = 0 } }
        };
        // 同上：等「第一步真的跑过」这个确定信号，而不是赌固定 150ms——推送模型下若在登记前就 CancelAll，
        // 这一次推送会整个落空（全局信号本身不再叫醒睡眠），测试要睡满 20 秒才失败。
        using var started = new ManualResetEventSlim(false);
        var deps = new GroupDeps { Hour = 10, IsoDay = 3, RunStep = s => { ran.Add(s.Label); started.Set(); } };
        var t = Task.Run(() => { started.Wait(5000); StopSignal.Request(); ActionGroupRunner.CancelAll(); });
        var sw = Stopwatch.StartNew();
        try { RunTopLevel(g, deps); }
        finally { await t; StopSignal.Clear(); }
        Assert.Single(ran);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"急停没叫醒轮间延迟，耗时 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void CancelAll_on_an_empty_registry_is_a_noop()
        => ActionGroupRunner.CancelAll();   // 没有在途运行时不得抛（急停在空闲时也会调）

    [Fact]
    public void Global_stop_still_stops_a_group_run()
    {
        // 回归闸：per-run 取消不能把全局急停架空——急停仍是停掉一切的总闸。
        var ran = new List<string>();
        var g = new ActionGroup
        {
            Id = "gc7",
            Steps = new()
            {
                new LaunchStep { Kind = "volume", Action = "mute", Label = "1", DelayMs = 0 },
                new LaunchStep { Kind = "volume", Action = "mute", Label = "2", DelayMs = 0 },
            }
        };
        var deps = new GroupDeps
        {
            Hour = 10, IsoDay = 3,
            RunStep = s => { ran.Add(s.Label); if (s.Label == "1") StopSignal.Request(); },
        };
        try { ActionGroupRunner.RunGroup(g, deps); }
        finally { StopSignal.Clear(); }
        Assert.Equal(new[] { "1" }, ran.ToArray());
    }

    [Fact]
    public void Comment_step_never_runs()
    {
        var ran = new List<string>();
        var g = new ActionGroup { Id = "gcm", Steps = new()
        {
            new LaunchStep { Kind = "comment", Label = "=== 第一段 ===" },
            new LaunchStep { Kind = "volume", Action = "mute", Label = "real" },
        } };
        ActionGroupRunner.RunGroup(g, Deps(ran));
        Assert.Equal(new[] { "real" }, ran.ToArray());
    }

    [Fact]
    public void Comment_step_consumes_no_budget()
    {
        var ran = new List<string>();
        var budget = new RunBudget();
        var g = new ActionGroup { Id = "gcb", Steps = new()
        {
            new LaunchStep { Kind = "comment", Label = "note" },
            new LaunchStep { Kind = "comment", Label = "note2" },
            new LaunchStep { Kind = "volume", Action = "mute", Label = "real" },
        } };
        var deps = new GroupDeps { Hour = 10, IsoDay = 3, RunStep = s => ran.Add(s.Label), Budget = budget };
        ActionGroupRunner.RunGroup(g, deps);
        Assert.Equal(new[] { "real" }, ran.ToArray());
        Assert.False(budget.Exhausted);
    }
}
