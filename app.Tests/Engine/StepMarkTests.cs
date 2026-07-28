using Clockwork.Engine;
using Clockwork.Core;
using Xunit;

public class StepMarkTests
{
    public StepMarkTests() => StopSignal.Clear();

    [Fact] public void MarkOf_ok() { var m = StepRunner.MarkOf(ActionResult.Empty); Assert.Equal("✓", m.Mark); Assert.Equal(0, m.Fail); }
    [Fact] public void MarkOf_unverified() { var m = StepRunner.MarkOf(ActionResult.Unver()); Assert.Equal("~ 已发送（未校验）", m.Mark); Assert.Equal(1, m.Unver); }
    [Fact] public void MarkOf_warning() { var m = StepRunner.MarkOf(ActionResult.Warn("坏了")); Assert.Equal("⚠ 坏了", m.Mark); Assert.Equal(1, m.Fail); }

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
}
