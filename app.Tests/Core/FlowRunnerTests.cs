using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// 真的跑 ActionGroupRunner：块循环、循环变量、跳出、变量条件。
//
// 这里只跑不碰系统状态的步骤（RunStep 只记录一下跑到了哪一步）。挂系统的那些
// （按键注入、开文件）各有各的实测，不该由单元测试去驱动真实键盘和资源管理器。

public class FlowRunnerTests
{
    // 会产出变量的那几步在这里手工兑现：真正的产出路径在 StepRunner 里（要系统状态），
    // 而这个文件要验的是**编排**——谁跑了几遍、变量在不在、跳出走没走出去。
    private static void Produce(LaunchStep s, RunVars vars)
    {
        switch (s.Kind)
        {
            case "splitLines": vars.SetList(s.OutputVar, (s.Text ?? "").Replace("\r\n", "\n").Split('\n')); break;
            case "math": vars.Set(s.OutputVar, StepMath.Evaluate(s.Text).Value); break;
        }
    }

    private static LaunchStep St(string kind, string parent = "", Action<LaunchStep>? cfg = null)
    {
        var s = new LaunchStep
        {
            Id = Guid.NewGuid().ToString(), Kind = kind, ParentId = parent,
            OutputVar = "", Pattern = "", Text = "",
        };
        cfg?.Invoke(s);
        return s;
    }

    // 返回一个记录而不是 out 参数：RunStep 是 lambda，而 C# 不许 lambda 捕获 out 参数
    // （CS1628）。这一条是写出来才发现的——先写成了三个 out，编译器直接不让过。
    private sealed record Ran(List<LaunchStep> Order, RunVars Vars, List<string> Skips, GroupRunResult Result);

    private static Ran Go(params LaunchStep[] steps)
    {
        var order = new List<LaunchStep>();
        var vars = new RunVars();
        var skips = new List<string>();
        var deps = new GroupDeps
        {
            Vars = vars,
            RunStep = s => { order.Add(s); Produce(s, vars); },
            OnStepSkipped = (_, k) => skips.Add(k.Key),
        };
        var r = ActionGroupRunner.RunGroup(new ActionGroup { Id = "g1", Name = "t", Steps = steps.ToList() }, deps);
        return new Ran(order, vars, skips, r);
    }

    /// <summary>把两段文本拆成一个列表变量 —— 每个循环用例都得先有它。</summary>
    private static LaunchStep Lines(string varName, string text)
        => St("splitLines", cfg: s => { s.OutputVar = varName; s.Text = text; });

    // 「每个」跑的是它的孩子，而**不是**后面那些同层的步骤。
    [Fact]
    public void Each_runs_only_its_children()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var inBody = St("waitKey", block.Id, s => s.OutputVar = "seen");
        var outside = St("waitKey");

        var order = Go(Lines("lines", "a\nb\nc"), block, inBody, outside).Order;

        Assert.Equal(3, order.Count(x => ReferenceEquals(x, inBody)));
        Assert.Single(order.Where(x => ReferenceEquals(x, outside)));
    }

    [Fact]
    public void Each_iterates_the_list_items_in_order()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var body = St("waitKey", block.Id);

        var seen = new List<string>();
        var vars = new RunVars();
        var deps = new GroupDeps
        {
            Vars = vars,
            RunStep = s => { if (ReferenceEquals(s, body)) seen.Add(vars.Get("item") ?? "<null>"); Produce(s, vars); },
        };
        ActionGroupRunner.RunGroup(new ActionGroup
        {
            Id = "g", Name = "t",
            Steps = { Lines("lines", "a\nb\nc"), block, body },
        }, deps);

        Assert.Equal(new[] { "a", "b", "c" }, seen);
    }

    [Fact]
    public void The_index_variable_counts_from_zero()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var body = St("waitKey", block.Id);
        var vars = new RunVars();
        var idx = new List<string>();
        var deps = new GroupDeps
        {
            Vars = vars,
            RunStep = s => { if (ReferenceEquals(s, body)) idx.Add(vars.Get("index") ?? "?"); Produce(s, vars); },
        };
        ActionGroupRunner.RunGroup(new ActionGroup
        {
            Id = "g", Name = "t",
            Steps = { Lines("lines", "a\nb"), block, body },
        }, deps);
        Assert.Equal(new[] { "0", "1" }, idx);
    }

    // 嵌套循环里两个 item 同名：内层跑完必须把外层的还回去。
    [Fact]
    public void A_nested_loop_restores_the_outer_loop_variables()
    {
        var outer = St("each", cfg: s => s.Text = "outerList");
        var outerBody = St("waitKey", outer.Id);
        var inner = St("each", outer.Id, s => s.Text = "innerList");
        var innerBody = St("waitKey", inner.Id);

        var vars = new RunVars();
        var log = new List<string>();
        var deps = new GroupDeps
        {
            Vars = vars,
            RunStep = s =>
            {
                // **只记循环体那两步**：顶层的两条「拆分为多行」也会走到 RunStep，
                // 而它们既不是 outerBody 也不是 innerBody，会被三元表达式归到 "i"。
                // 早先的版本就是这么把期望序列的第一项变成 "i=" 的。
                if (ReferenceEquals(s, outerBody) || ReferenceEquals(s, innerBody))
                    log.Add((ReferenceEquals(s, outerBody) ? "O" : "i") + "=" + vars.Get("item"));
                Produce(s, vars);
            },
        };
        ActionGroupRunner.RunGroup(new ActionGroup
        {
            Id = "g", Name = "t",
            Steps = { Lines("outerList", "A\nB"), Lines("innerList", "1\n2"), outer, outerBody, inner, innerBody },
        }, deps);

        Assert.Equal(new[] { "O=A", "i=1", "i=2", "O=B", "i=1", "i=2" }, log);
        // 两层都跑完之后 item **不存在**——最外层那个循环的 item 也是循环变量，
        // 出了循环就该还回去（它进来之前没有这个变量）。
        // 早先这里断言等于 "B"，那是把「内层还原了外层」和「外层还原了循环之外」混成了一件事。
        Assert.Null(vars.Get("item"));
    }

    // 跳出本块：块里后面的不跑，外层继续。
    [Fact]
    public void Skip_block_ends_the_body_but_the_outer_run_continues()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var before = St("waitKey", block.Id);
        var skip = St("skip", block.Id, s => s.SkipMode = "block");
        var after = St("waitKey", block.Id);
        var outside = St("waitKey");

        var order = Go(Lines("lines", "a\nb\nc"), block, before, skip, after, outside).Order;

        Assert.Equal(3, order.Count(x => ReferenceEquals(x, before)));
        Assert.DoesNotContain(order, x => ReferenceEquals(x, after));
        Assert.Single(order.Where(x => ReferenceEquals(x, outside)));
    }

    // 块里的「跳出本块」= 进入下一次迭代（continue），不是结束循环。
    [Fact]
    public void Skip_block_inside_a_loop_goes_to_the_next_iteration()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var before = St("waitKey", block.Id);
        var skip = St("skip", block.Id, s => s.SkipMode = "block");
        var after = St("waitKey", block.Id);

        var order = Go(Lines("lines", "a\nb\nc"), block, before, skip, after).Order;

        Assert.Equal(3, order.Count(x => ReferenceEquals(x, before)));
        Assert.DoesNotContain(order, x => ReferenceEquals(x, after));
    }

    // 「中止整个动作」一路往上：块里的中止会让整个组停下。
    [Fact]
    public void Skip_run_stops_the_whole_group()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var skip = St("skip", block.Id, s => s.SkipMode = "run");
        var outside = St("waitKey");

        var ran = Go(Lines("lines", "a\nb"), block, skip, outside);
        var order = ran.Order;

        Assert.DoesNotContain(order, x => ReferenceEquals(x, outside));
        Assert.Equal(GroupRunResult.Aborted, ran.Result);
    }

    // 变量条件：不满足就跳过这一步。
    [Fact]
    public void A_variable_condition_gates_the_step()
    {
        var yes = St("waitKey", cfg: s => { s.IfVar = "k"; s.IfVarOp = "eq"; s.IfVarValue = "Escape"; });
        var no = St("waitKey", cfg: s => { s.IfVar = "k"; s.IfVarOp = "eq"; s.IfVarValue = "Insert"; });

        var vars = new RunVars();
        vars.Set("k", "Escape");
        var order = new List<LaunchStep>();
        var deps = new GroupDeps { Vars = vars, RunStep = s => order.Add(s) };
        ActionGroupRunner.RunGroup(new ActionGroup { Id = "g", Name = "t", Steps = { yes, no } }, deps);

        Assert.Same(yes, Assert.Single(order));
    }

    // 条件引用的变量压根不存在 → 不成立 → 跳过。方向不能反：反过来会变成「靠它分流的两条都跑」。
    [Fact]
    public void A_condition_on_a_variable_that_does_not_exist_skips_the_step()
    {
        var step = St("waitKey", cfg: s => { s.IfVar = "never-produced"; s.IfVarOp = "notempty"; });
        var order = Go(step).Order;
        Assert.Empty(order);
    }

    // 一个不是列表的变量当列表用：如实说一句，然后什么都不做（不静默跑零遍，那看起来像成功）。
    [Fact]
    public void Each_over_a_non_list_says_so_and_runs_nothing()
    {
        var block = St("each", cfg: s => s.Text = "notAList");
        var body = St("waitKey", block.Id);
        var vars = new RunVars();
        vars.Set("notAList", "just text");

        var order = new List<LaunchStep>();
        var skips = new List<string>();
        var deps = new GroupDeps { Vars = vars, RunStep = s => order.Add(s), OnStepSkipped = (_, k) => skips.Add(k.Key) };
        ActionGroupRunner.RunGroup(new ActionGroup
        {
            Id = "g", Name = "t",
            Steps = { Lines("lines", "a"), block, body },
        }, deps);

        // 只断言**循环体那一步**没跑。顶层的「拆分为多行」本来就该跑，
        // 连它一起断言「什么都没跑」是这条测试自己写错了一次。
        Assert.DoesNotContain(order, x => ReferenceEquals(x, body));
        Assert.Equal(new[] { "Skip_EachNoList" }, skips);
    }

    // 空列表：跑零遍，**不算错**——那正是「没有要处理的东西」的意思。
    //
    // 用 SetList 直接给一个空列表，而不是拿空文本去拆：
    // 空文本会被 StepRunner 判成「没有可拆的文本」而报错，而就算拆得出来，
    // "".Split('\n') 给的是**一个空字符串**（那确实是一行空白行），不是零行。
    [Fact]
    public void Each_over_an_empty_list_does_nothing_without_complaining()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var body = St("waitKey", block.Id);
        var vars = new RunVars();
        vars.SetList("lines", Array.Empty<string>());

        var order = new List<LaunchStep>();
        var skips = new List<string>();
        var deps = new GroupDeps { Vars = vars, RunStep = s => order.Add(s), OnStepSkipped = (_, k) => skips.Add(k.Key) };
        ActionGroupRunner.RunGroup(new ActionGroup { Id = "g", Name = "t", Steps = { block, body } }, deps);

        Assert.Empty(order);
        Assert.Empty(skips);
    }

    // 不写列表 = 按「重复次数」跑。这让「每个」在没有列表时仍是一个可用的循环块。
    [Fact]
    public void Each_without_a_list_repeats_by_the_repeat_count()
    {
        var block = St("each", cfg: s => s.Repeat = 3);
        var body = St("waitKey", block.Id);
        var order = Go(block, body).Order;
        Assert.Equal(3, order.Count(x => ReferenceEquals(x, body)));
    }

    // 关掉块的某一步：那一轮跳过它，其余照跑。
    [Fact]
    public void A_disabled_step_inside_a_block_is_skipped_in_every_iteration()
    {
        var block = St("each", cfg: s => s.Text = "lines");
        var off = St("waitKey", block.Id, s => s.Enabled = false);
        var on = St("waitKey", block.Id);

        var order = Go(Lines("lines", "a\nb"), block, off, on).Order;

        Assert.DoesNotContain(order, x => ReferenceEquals(x, off));
        Assert.Equal(2, order.Count(x => ReferenceEquals(x, on)));
    }
}
