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
}
