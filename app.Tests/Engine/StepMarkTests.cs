using System.IO;
using Clockwork.Engine;
using Clockwork.Core;
using Xunit;

public class StepMarkTests
{
    public StepMarkTests() => StopSignal.Clear();

    [Fact] public void MarkOf_ok() { var m = StepRunner.MarkOf(ActionResult.Empty); Assert.Equal("✓", m.Mark); Assert.Equal(0, m.Fail); }
    [Fact] public void MarkOf_unverified() { var m = StepRunner.MarkOf(ActionResult.Unver()); Assert.Equal("~ 已发送（未校验）", m.Mark); Assert.Equal(1, m.Unver); }
    // 实参必须是 resx 键、不是渲染好的句子。这里从前写的是 Warn("坏了")——它一直是绿的，
    // 因为 Strings.Get 取不到时原样返回键名，于是「把句子当键传」这种误用看起来完全正常。
    // Warn 改成 (key, args) 之后那种写法照样编译得过，只有这条断言换成走真键才盯得住。
    [Fact]
    public void MarkOf_warning()
    {
        var m = StepRunner.MarkOf(ActionResult.Warn("Warn_AskNoUi"));
        Assert.Equal("⚠ " + Clockwork.I18n.Strings.Get("Warn_AskNoUi"), m.Mark);
        Assert.NotEqual("⚠ Warn_AskNoUi", m.Mark);   // 键真的被翻出来了，不是原样回落
        Assert.Equal(1, m.Fail);
    }

    [Fact]
    public void Aggregate_all_ok()
    {
        var m = StepRunner.AggregateRepeat(3, _ => new StepMark("✓", 0, 0), 0);
        Assert.Equal("✓", m.Mark);
    }

    [Fact]
    public void Aggregate_first_nonok_wins_and_counts_accumulate()
    {
        var m = StepRunner.AggregateRepeat(3, i => i == 2 ? new StepMark("⚠ x", 1, 0) : new StepMark("✓", 0, 0), 0);
        Assert.Equal("⚠ x", m.Mark);
        Assert.Equal(1, m.Fail);
    }

    [Fact]
    public void Aggregate_stops_on_signal()
    {
        int calls = 0;
        StopSignal.Request();
        var m = StepRunner.AggregateRepeat(5, _ => { calls++; return new StepMark("✓", 0, 0); }, 10);
        Assert.Equal(1, calls);   // 第1次跑完，次间检测急停即停
        StopSignal.Clear();
    }

    // —— 「已在运行则激活」与「参数」的取舍 ——
    // 本选项默认开启。带参数时若还走激活捷径，RunLaunchItem 会在读到 Args 之前就 return，
    // 于是「用记事本打开 a.txt」在记事本已开着时只是把旧窗口带到前台、文件根本没打开，还记成 ✓。
    [Fact]
    public void Activate_shortcut_is_skipped_when_the_step_carries_arguments()
    {
        var withArgs = new LaunchStep { Kind = "app", Target = "notepad.exe", Args = @"D:\a.txt" };
        Assert.True(withArgs.ActivateIfRunning);                                   // 默认开
        Assert.False(StepRunner.ShouldActivateInsteadOfLaunch(withArgs));          // 但带参数 → 照常启动

        var noArgs = new LaunchStep { Kind = "app", Target = "notepad.exe" };
        Assert.True(StepRunner.ShouldActivateInsteadOfLaunch(noArgs));             // 不带参数 → 激活已有窗口
    }

    [Fact]
    public void Activate_shortcut_still_off_when_the_flag_is_off()
        => Assert.False(StepRunner.ShouldActivateInsteadOfLaunch(
               new LaunchStep { Kind = "app", Target = "notepad.exe", ActivateIfRunning = false }));

    // 目标留空时要说人话。
    //
    // 编辑器是**允许**留空保存的（那是有意的一条：空白行在列表里显示成「（未指定）」而不是像坏了），
    // 所以这一步只能拦在运行期。从前 app 这一支没有这道闸：空文件名让 Process.Start 抛一句
    // .NET 的英文（"a file name has not been provided"），那句在中文界面里读不懂，
    // 也说不出下一步该做什么。url / path 两支早就有这道闸（「（未指定）」），app 这支补上，三支同一口径。
    [Fact]
    public void An_empty_launch_target_fails_in_the_users_language()
    {
        var r = StepRunner.RunLaunchItem(new LaunchStep { Kind = "app", Target = "" }, new List<string>());

        Assert.True(r.HasWarning);
        string expected = Clockwork.I18n.Strings.Get("Sum_Unset");
        Assert.Contains(expected, r.Warning!);
        Assert.DoesNotContain("file name has not been provided", r.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    // 三支（启动程序 / 打开网址 / 打开文件或文件夹）空目标时说同一句话。
    [Fact]
    public void All_three_launch_kinds_report_an_empty_target_the_same_way()
    {
        var app = StepRunner.RunLaunchItem(new LaunchStep { Kind = "app", Target = "" }, new List<string>());
        var url = StepRunner.InvokeStepAction(new LaunchStep { Kind = "url", Target = "" }, null, new List<string>(), null);
        var path = StepRunner.InvokeStepAction(new LaunchStep { Kind = "path", Target = "" }, null, new List<string>(), null);

        string expected = Clockwork.I18n.Strings.Get("Sum_Unset");
        Assert.Contains(expected, app.Warning!);
        Assert.Contains(expected, url.Warning!);
        Assert.Contains(expected, path.Warning!);
    }

    // 空目标那道闸必须排在「已在运行则激活」**之后**。
    //
    // 目标空着、而「激活进程」填了名字的那一步，要的正是「它开着就把窗口叫到前面」——
    // 闸若排在前面，那条路就被堵死了（本条修复自己差点引进这个回归）。
    // 走源码断言而不是真去激活一个窗口：那边要么依赖「某个进程正开着」这种环境前提，
    // 要么就得为它拉一个窗口出来，代价远大于这条断言要守住的东西。
    [Fact]
    public void The_empty_target_guard_sits_after_the_activate_branch()
    {
        var src = File.ReadAllText(Path.Combine(AppRoot(), "app", "Engine", "StepRunner.cs"));
        // 锚在**声明**上：这个名字在文件前面还被调用过若干次，锚在第一次出现会量到别处。
        int method = src.IndexOf("public static ActionResult RunLaunchItem", StringComparison.Ordinal);
        int activate = src.IndexOf("ShouldActivateInsteadOfLaunch(item)", method, StringComparison.Ordinal);
        int guard = src.IndexOf("Sum_Unset", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && activate > method && guard > method, "找不到 RunLaunchItem 里的那两处");
        Assert.True(guard > activate, "空目标的闸必须排在「已在运行则激活」之后");
    }

    private static string AppRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }
}
