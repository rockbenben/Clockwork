using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Core;
using Xunit;

// 单次运行的取消闸。与 StopSignalTests 的分工：那边测「全局急停」，这边测「只停这一次」——
// 两者的关系（本闸被取消 或 全局急停置位，都算停）正是这里要钉死的，否则一改就会退化成又一个全局开关。
public class RunCancelTests
{
    public RunCancelTests() => StopSignal.Clear();   // 进程内单例，每例前复位

    [Fact]
    public void Fresh_token_is_not_stopped()
    {
        var c = new RunCancel();
        Assert.False(c.IsRequested);
        Assert.False(c.IsStopped);
    }

    // 取消一份运行不能波及另一份：热键取消「专注」时，正在跑的「收工」必须继续。
    [Fact]
    public void Cancel_is_scoped_to_one_token()
    {
        var a = new RunCancel();
        var b = new RunCancel();
        a.Request();
        Assert.True(a.IsStopped);
        Assert.False(b.IsStopped);
        Assert.False(StopSignal.IsRequested);   // 单组取消绝不置位全局急停
    }

    // 反向：全局急停是总闸，任何在跑的运行都得停——这是急停的保命语义，不能被 per-run 取消架空。
    [Fact]
    public void Global_stop_stops_every_token()
    {
        var c = new RunCancel();
        StopSignal.Request();
        try
        {
            Assert.True(c.IsStopped);
            Assert.False(c.IsRequested);   // 但「这一份被取消了吗」仍是 false：两个来源要分得开
        }
        finally { StopSignal.Clear(); }
    }

    [Fact]
    public void Sleep_zero_reports_current_state()
    {
        var c = new RunCancel();
        Assert.True(c.InterruptibleSleep(0));
        c.Request();
        Assert.False(c.InterruptibleSleep(0));
    }

    [Fact]
    public void Sleep_completes_returns_true()
        => Assert.True(new RunCancel().InterruptibleSleep(30));

    // 本类拿 StopSignal 的底层 WaitHandle 去 WaitAny，整个设计押在「Slim 事件 Reset 后，底层句柄
    // 也跟着复位」上。若两者脱钩，急停一次之后每个 RunCancel 的等待都会永远立刻返回——所有延时静默失效，
    // 而且没有任何报错。这条用例专钉这个假设。
    [Fact]
    public void Sleep_still_works_after_a_global_stop_was_cleared()
    {
        StopSignal.Request();
        StopSignal.Clear();
        var c = new RunCancel();
        Assert.False(c.IsStopped);
        var sw = Stopwatch.StartNew();
        Assert.True(c.InterruptibleSleep(120));   // 必须真睡满，而不是被残留的置位句柄立刻叫醒
        Assert.True(sw.ElapsedMilliseconds >= 100, $"只睡了 {sw.ElapsedMilliseconds}ms，句柄没跟着复位");
    }

    [Fact]
    public void Sleep_returns_false_immediately_when_already_cancelled()
    {
        var c = new RunCancel();
        c.Request();
        var sw = Stopwatch.StartNew();
        Assert.False(c.InterruptibleSleep(5000));
        Assert.True(sw.ElapsedMilliseconds < 1000, $"已取消却仍睡了 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Sleep_returns_false_immediately_when_global_stop_already_set()
    {
        var c = new RunCancel();
        StopSignal.Request();
        try
        {
            var sw = Stopwatch.StartNew();
            Assert.False(c.InterruptibleSleep(5000));
            Assert.True(sw.ElapsedMilliseconds < 1000, $"已急停却仍睡了 {sw.ElapsedMilliseconds}ms");
        }
        finally { StopSignal.Clear(); }
    }

    // 睡到一半才按下取消：必须当场醒。只在进入前查一次是不够的——组的 RepeatDelayMs 可以是几十分钟，
    // 那种「按了取消要等这一觉睡完」的实现在用户眼里和没取消没有区别。
    [Fact]
    public async Task Sleep_wakes_up_when_cancelled_midway()
    {
        var c = new RunCancel();
        var t = Task.Run(() => { Thread.Sleep(120); c.Request(); });
        var sw = Stopwatch.StartNew();
        Assert.False(c.InterruptibleSleep(10_000));
        await t;
        Assert.True(sw.ElapsedMilliseconds < 3000, $"取消后没醒，睡了 {sw.ElapsedMilliseconds}ms");
    }

    // 全局急停是「推」进来的，不是本闸去「拉」的：InterruptibleSleep 只等自己那一个事件，
    // 急停由 App.RequestStop → ActionGroupRunner.CancelAll 把 Request() 推给每个在途闸。
    // 这样全局信号全程只有 IsRequested 一个读法，不会出现「内核句柄置位、IsSet 为假」的永久分歧。
    // 本例锁定推送这一半：睡到一半被推送即刻醒。
    [Fact]
    public async Task Sleep_wakes_up_when_a_global_stop_is_pushed_in()
    {
        var c = new RunCancel();
        var t = Task.Run(() => { Thread.Sleep(120); StopSignal.Request(); c.Request(); });   // RequestStop 的等价动作
        var sw = Stopwatch.StartNew();
        try
        {
            Assert.False(c.InterruptibleSleep(10_000));
            await t;
            Assert.True(sw.ElapsedMilliseconds < 3000, $"急停推送后没醒，睡了 {sw.ElapsedMilliseconds}ms");
        }
        finally { StopSignal.Clear(); }
    }

    // 反向锁定：本闸绝不去等全局信号的内核句柄。曾经的实现用 WaitAny 同时等两个句柄，
    // 而 ManualResetEventSlim 的 Set/Reset 对「托管状态位」与「内核事件」不是原子的——
    // Request/Clear 交错能留下句柄恒置位、IsSet 为假的永久分歧，此后每个带延时的组都会在第一步
    // 静默截断还报 Completed。这里制造那种残留（置位再清空，句柄可能仍是置位态），要求睡眠照常睡满。
    [Fact]
    public void Sleep_is_unaffected_by_a_stale_global_handle()
    {
        StopSignal.Request();
        StopSignal.Clear();
        var c = new RunCancel();
        Assert.False(c.IsStopped);
        var sw = Stopwatch.StartNew();
        Assert.True(c.InterruptibleSleep(150));
        Assert.True(sw.ElapsedMilliseconds >= 120, $"只睡了 {sw.ElapsedMilliseconds}ms，疑似被残留句柄叫醒");
    }
}
