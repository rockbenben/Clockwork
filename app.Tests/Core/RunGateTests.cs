using System.Threading;
using System.Threading.Tasks;
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

    // 并发 Begin 不得漏出「陈旧急停」。计数与 0→1 的 Clear 若不在同一临界区，输掉 0→1 竞争的那一路
    // 会在 Clear 之前就返回并开跑，第一步撞上尚未清掉的旧急停 → 整组零步退出、还报 Completed，
    // 用户看到的是「到点了那个组什么都没做，也没有任何提示」。改成 Interlocked 单独用即当场复现。
    [Fact]
    public async Task Concurrent_begin_never_leaks_a_stale_stop()
    {
        int leaked = 0;
        for (int round = 0; round < 3000 && leaked == 0; round++)
        {
            var gate = new RunGate();
            StopSignal.Request();          // 上一次急停留下的置位（没有任何东西会清它，只等下一路 0→1）
            using var start = new ManualResetEventSlim(false);
            int observed = 0;
            var tasks = new Task[8];
            for (int i = 0; i < tasks.Length; i++)
                tasks[i] = Task.Run(() =>
                {
                    start.Wait();
                    gate.Begin();
                    // Begin 返回 = 这一路马上要执行步骤了，此刻绝不该还看得到急停置位
                    if (StopSignal.IsRequested) Interlocked.Increment(ref observed);
                    gate.End();
                });
            start.Set();
            await Task.WhenAll(tasks);
            leaked = observed;
        }
        StopSignal.Clear();
        Assert.True(leaked == 0, $"有 {leaked} 路运行在 Begin 返回后仍看到陈旧急停");
    }
}
