using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// 「列表」与「合」——把列表这条链补全的两步。
//
// 补它们之前，列表是这个程序里唯一**不能凭空从文本得到**的形状：文本由 prompt /
// 浏览器地址 / 正则 / 替换……十来种方式产出，列表此前只有「把一段多行文本拆开」
// 这一条降级路径，而多行文本又没有一步能造（prompt 是单行框）。于是循环功能在
// 界面上第一步就走不出去。这两条把链闭上：填 → 拆 → 合 → 用。

public class ListChainTests
{
    private static LaunchStep St(string kind, Action<LaunchStep>? cfg = null)
    {
        var s = new LaunchStep { Id = Guid.NewGuid().ToString(), Kind = kind, Enabled = true };
        cfg?.Invoke(s);
        return s;
    }

    // ---- 拆：三种换行写法都认，且空行保留 ----

    [Theory]
    [InlineData("a\nb", new[] { "a", "b" })]
    [InlineData("a\r\nb", new[] { "a", "b" })]
    [InlineData("a\rb", new[] { "a", "b" })]
    public void Every_line_ending_splits_the_same_way(string text, string[] want)
        => Assert.Equal(want, StepHelpers.SplitLines(text));

    [Fact]
    public void Blank_lines_survive_the_split()
    {
        // 往终端里逐行敲命令时，空行常常就是要发出去的那一条。滤掉会让行号与
        // 用户以为的错位，而错位的原因在界面上完全看不出来。
        Assert.Equal(new[] { "a", "", "b" }, StepHelpers.SplitLines("a\n\nb"));
    }

    [Fact]
    public void Splitting_nothing_gives_one_empty_line_not_zero()
    {
        // "" 按行拆就是一个空行（那确实是一行空白）。用户要的是零行的话，
        // 正确答案是「别调它」——所以 StepRunner 在文本为空时先报一次错。
        Assert.Equal(new[] { "" }, StepHelpers.SplitLines(""));
    }

    // ---- 填：「列表」步骤把用户的回答按行收成列表，而不是留一段中间字符串 ----

    [Fact]
    public void The_list_step_stores_lines_directly_and_no_intermediate_text()
    {
        var order = new List<LaunchStep>();
        var vars = new RunVars();
        var deps = new GroupDeps
        {
            Vars = vars,
            AskUser = _ => "one\ntwo\nthree",
            RunStep = s => order.Add(s),
        };

        var ask = St("list", s => { s.Message = "填几行"; s.OutputVar = "行"; });
        ActionGroupRunner.RunGroup(new ActionGroup { Id = "g", Name = "t", Steps = { ask } }, deps);

        // 不留那个字符串：留住它就要用户自己再接一次「拆分为多行」，
        // 而「我要一个列表」本来就不该还要他们自己动手拆一道。
        Assert.Null(vars.Get("行"));
        Assert.Equal(new[] { "one", "two", "three" }, vars.GetList("行"));
    }

    [Fact]
    public void Cancelling_the_list_step_aborts_the_group()
    {
        var vars = new RunVars();
        var ran = new List<LaunchStep>();
        var deps = new GroupDeps
        {
            Vars = vars,
            AskUser = _ => null,                       // 用户取消
            RunStep = s => ran.Add(s),
        };

        var ask = St("list", s => s.OutputVar = "行");
        var after = St("message");
        var r = ActionGroupRunner.RunGroup(
            new ActionGroup { Id = "g", Name = "t", Steps = { ask, after } }, deps);

        Assert.Equal(GroupRunResult.Aborted, r);
        Assert.Empty(ran);
        Assert.False(vars.Has("行"));                 // 取消的答案连记下来都没有意义
    }

    // ---- 合：列表 → 文本，这是循环攒下的结果走得出去的唯一出口 ----
    //
    // 这一组**直调 StepRunner**，不走 ActionGroupRunner。产出变量的活儿在 StepRunner 里，
    // 而编排器只负责「什么时候跑这一步」——先绕编排器再断言产出，等于用一条空实现的
    // RunStep 去验证一件根本没发生的事（头一版就是这么写的，join 的产出自然是 null）。

    private static string? RunJoin(RunVars vars, string listName, string sep, string outVar)
    {
        var step = new LaunchStep
        {
            Id = Guid.NewGuid().ToString(), Kind = "join", Enabled = true,
            Text = listName, Separator = sep, OutputVar = outVar,
        };
        var r = StepRunner.InvokeStepAction(step, null, [], null, vars);
        Assert.False(r.HasWarning, r.Warning);
        return vars.Get(outVar);
    }

    [Fact]
    public void Join_puts_the_items_back_into_one_piece_of_text()
    {
        var vars = new RunVars();
        vars.SetList("urls", new[] { "a.example", "b.example", "c.example" });
        Assert.Equal("a.example\nb.example\nc.example", RunJoin(vars, "urls", "\n", "全部"));
    }

    [Fact]
    public void Join_with_an_empty_separator_concatenates_with_nothing_between()
    {
        var vars = new RunVars();
        vars.SetList("x", new[] { "a", "b" });
        Assert.Equal("ab", RunJoin(vars, "x", "", "y"));
    }

    // 没填名字与填了但那不是列表，是两回事：前者是「这一步还没配」，
    // 后者是「前面那一步产出的不是列表」。合成一句话的话，用户读到
    // 「列表变量「」不是列表」会去看自己的配置，而不是去看上一环节。
    [Fact]
    public void Join_reports_a_missing_name_differently_from_a_wrong_type()
    {
        var vars = new RunVars();
        vars.Set("notAList", "just text");

        ActionResult NoName() => StepRunner.InvokeStepAction(
            new LaunchStep { Kind = "join", Text = "" }, null, [], null, vars);
        ActionResult WrongType() => StepRunner.InvokeStepAction(
            new LaunchStep { Kind = "join", Text = "notAList" }, null, [], null, vars);

        var a = NoName().Warning;
        var b = WrongType().Warning;

        Assert.True(a != b, "两条提示是同一句：用户会去看自己的配置，而不是去看上一环节");
        Assert.Equal(Clockwork.I18n.Strings.Get("Warn_JoinNoName"), a);
        // 第二条带上了那个变量名——用户要照着它去找前面那一步
        Assert.Contains("notAList", b ?? "");
    }

    // ---- 闭环：填 → 用 → 合，全程不经过编辑配置文件 ----
    //
    // 循环体用「算一步」而不是「列表」：list 那类问句步骤会被编排器截走问用户，
    // 不走 RunStep——把「记录当前项」写在 RunStep 里就永远等不到它（前头一版就这么白跑了一轮）。

    [Fact]
    public void A_loop_can_accumulate_and_hand_the_result_on()
    {
        var vars = new RunVars();
        var block = St("each", s => s.Text = "行");
        var body = St("math", s => { s.Text = "1+1"; s.OutputVar = "n"; });
        body.ParentId = block.Id;

        var seen = new List<string>();
        var deps = new GroupDeps
        {
            Vars = vars,
            AskUser = _ => "a\nb\nc",
            RunStep = s =>
            {
                if (!ReferenceEquals(s, body)) return;
                seen.Add(vars.Get("item") ?? "<null>");
                // 模拟循环体攒结果：把当前项追加到「结果」列表
                var acc = vars.GetList("结果") ?? Array.Empty<string>();
                vars.SetList("结果", acc.Concat(new[] { vars.Get("item") ?? "" }).ToList());
            },
        };

        var ask = St("list", s => { s.Message = "填几行"; s.OutputVar = "行"; });

        ActionGroupRunner.RunGroup(
            new ActionGroup { Id = "g", Name = "t", Steps = { ask, block, body } }, deps);

        Assert.Equal(new[] { "a", "b", "c" }, seen);
        // 攒下的结果走得出去——这是「合」存在的全部理由
        Assert.Equal("a,b,c", RunJoin(vars, "结果", ",", "串"));
    }
}
