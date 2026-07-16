using Clockwork.Engine;
using Clockwork.Core;
using Xunit;

public class ReadyGateTests
{
    public ReadyGateTests() => StopSignal.Clear();

    [Fact]
    public void Ready_immediately_zero_wait()
    {
        var r = ReadyGate.WaitSystemReady(90, true, 500, () => true, () => true, _ => { });
        Assert.True(r.Ready);
        Assert.Equal(0, r.WaitedMs);
    }

    [Fact]
    public void Waits_until_both_ready()
    {
        int shellCalls = 0;
        var r = ReadyGate.WaitSystemReady(90, true, 100, () => ++shellCalls >= 3, () => true, _ => { });
        Assert.True(r.Ready);
    }

    [Fact]
    public void Timeout_passes_through_not_ready()
    {
        var r = ReadyGate.WaitSystemReady(1, true, 500, () => false, () => true, _ => { });
        Assert.False(r.Ready);
        Assert.Equal(1000, r.WaitedMs);
    }

    [Fact]
    public void No_network_required_ready_on_shell()
    {
        var r = ReadyGate.WaitSystemReady(90, false, 100, () => true, () => false, _ => { });
        Assert.True(r.Ready);
    }

    [Fact]
    public void Probe_exception_treated_ready()
    {
        var r = ReadyGate.WaitSystemReady(90, true, 100, () => throw new Exception(), () => throw new Exception(), _ => { });
        Assert.True(r.Ready);
    }

    [Fact]
    public void Stop_breaks()
    {
        StopSignal.Request();
        var r = ReadyGate.WaitSystemReady(90, true, 100, () => false, () => false, _ => { });
        Assert.False(r.Ready);
        StopSignal.Clear();
    }
}
