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

    // 急停按钮靠这个事件决定显示/隐藏：每次 Begin/End 都得响一次，
    // 且回调里读到的 Active 必须已是新值（漏掉最后一次 End 会让按钮永远留在界面上）。
    [Fact]
    public void ActiveChanged_fires_on_every_begin_and_end()
    {
        var gate = new RunGate();
        var seen = new List<int>();
        gate.ActiveChanged += () => seen.Add(gate.Active);

        gate.Begin();   // 1
        gate.Begin();   // 2
        gate.End();     // 1
        gate.End();     // 0

        Assert.Equal(new[] { 1, 2, 1, 0 }, seen);
        StopSignal.Clear();
    }

    [Fact]
    public void ActiveChanged_without_subscriber_does_not_throw()
    {
        var gate = new RunGate();
        gate.Begin();
        gate.End();
        Assert.Equal(0, gate.Active);
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
