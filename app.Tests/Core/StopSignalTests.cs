using Clockwork.Core;
using Xunit;

public class StopSignalTests
{
    public StopSignalTests() => StopSignal.Clear(); // 每例前复位（进程内单例）

    [Fact] public void Not_requested_by_default() => Assert.False(StopSignal.IsRequested);

    [Fact]
    public void Request_then_isRequested()
    {
        StopSignal.Request();
        Assert.True(StopSignal.IsRequested);
        StopSignal.Clear();
    }

    [Fact]
    public void Clear_resets()
    {
        StopSignal.Request();
        StopSignal.Clear();
        Assert.False(StopSignal.IsRequested);
    }

    [Fact] public void Sleep_zero_returns_true_when_clear() => Assert.True(StopSignal.InterruptibleSleep(0));

    [Fact]
    public void Sleep_zero_returns_false_when_requested()
    {
        StopSignal.Request();
        Assert.False(StopSignal.InterruptibleSleep(0));
        StopSignal.Clear();
    }

    [Fact] public void Sleep_completes_returns_true() => Assert.True(StopSignal.InterruptibleSleep(30)); // 睡满 30ms

    [Fact]
    public void Sleep_interrupted_returns_false()
    {
        StopSignal.Request();  // 已置位 → 立即被打断
        Assert.False(StopSignal.InterruptibleSleep(5000));
        StopSignal.Clear();
    }
}
