using Clockwork.Core;
using Xunit;

public class RunGateTests
{
    public RunGateTests() => StopSignal.Clear();   // 进程内单例，每例前复位

    [Fact]
    public void First_run_clears_stale_stop()
    {
        StopSignal.Request();                 // 上一次运行留下的急停
        var gate = new RunGate();
        gate.Begin();
        Assert.False(StopSignal.IsRequested); // 首个运行(0→1)清空
        Assert.Equal(1, gate.Active);
        gate.End();
        StopSignal.Clear();
    }

    [Fact]
    public void Concurrent_run_does_not_wipe_inflight_stop()
    {
        var gate = new RunGate();
        gate.Begin();                         // 运行 A 开跑
        StopSignal.Request();                 // 用户在 A 运行中按下急停
        gate.Begin();                         // 运行 B 并发开跑
        Assert.True(StopSignal.IsRequested);  // B 不得抹掉 A 的在途急停（本次修复的核心）
        Assert.Equal(2, gate.Active);
        gate.End(); gate.End();
        StopSignal.Clear();
    }

    [Fact]
    public void Fresh_run_after_all_finished_clears_again()
    {
        var gate = new RunGate();
        gate.Begin(); StopSignal.Request(); gate.End();  // 运行结束时急停仍置位
        Assert.True(StopSignal.IsRequested);
        gate.Begin();                                    // 下一路全新运行(0→1)重新清空
        Assert.False(StopSignal.IsRequested);
        gate.End();
        StopSignal.Clear();
    }
}
