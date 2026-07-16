using Clockwork.Native;
using Clockwork.Core;
using Xunit;

public class WaitAppWindowTests
{
    public WaitAppWindowTests() => StopSignal.Clear();

    [Fact]
    public void Present_immediately_zero_wait()
    {
        var r = WindowManager.WaitAppWindow(5, 100, () => true, _ => { });
        Assert.True(r.Present);
        Assert.Equal(0, r.WaitedMs);
    }

    [Fact]
    public void Timeout_gives_up()
    {
        int slept = 0;
        var r = WindowManager.WaitAppWindow(1, 500, () => false, ms => slept += ms);
        Assert.False(r.Present);
        Assert.Equal(1000, r.WaitedMs);
    }

    [Fact]
    public void Appears_after_two_polls()
    {
        int calls = 0;
        var r = WindowManager.WaitAppWindow(5, 100, () => ++calls >= 3, _ => { });
        Assert.True(r.Present);
    }

    [Fact]
    public void Stop_breaks_wait()
    {
        StopSignal.Request();
        var r = WindowManager.WaitAppWindow(10, 100, () => false, _ => { });
        Assert.False(r.Present);
        StopSignal.Clear();
    }
}
