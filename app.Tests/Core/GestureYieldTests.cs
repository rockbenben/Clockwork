using Clockwork.Core;
using Xunit;

// 「让路」的判据与计时器。它决定手势是否暂时不抢右键——判错一边是「卡住时继续吞键」，
// 另一边是「莫名其妙地把手势关了」，两边都只能靠日志和状态行事后发现。
public class GestureYieldTests
{
    private const int Threshold = 300;

    // 三条进入判据各自单独成立；只有「连续推迟一次」还不够。
    [Fact]
    public void Each_trigger_alone_is_enough_but_one_defer_is_not()
    {
        Assert.True(GestureYieldRules.ShouldTrigger(stranded: true, healDeferred: 0, 0, 0, Threshold));
        Assert.True(GestureYieldRules.ShouldTrigger(false, healDeferred: 2, 0, 0, Threshold));
        Assert.False(GestureYieldRules.ShouldTrigger(false, healDeferred: 1, 0, 0, Threshold));
        Assert.True(GestureYieldRules.ShouldTrigger(false, 0, callbackWindowMaxMs: Threshold, 0, Threshold));
        Assert.True(GestureYieldRules.ShouldTrigger(false, 0, 0, replayWindowMaxMs: Threshold, Threshold));
        Assert.False(GestureYieldRules.ShouldTrigger(false, 0, Threshold - 1, Threshold - 1, Threshold));
        // 阈值未知（0）时不能把「0ms 的峰值」当成拥堵：那是没读到预算，不是真有高峰。
        Assert.False(GestureYieldRules.ShouldTrigger(false, 0, 999, 999, thresholdMs: 0));
    }

    [Fact]
    public void Enters_for_four_seconds_then_leaves_when_the_trigger_clears()
    {
        var y = new GestureYield();
        Assert.Equal(GestureYieldChange.None, y.Tick(1_000, trigger: false));
        Assert.Equal(GestureYieldChange.Entered, y.Tick(1_000, trigger: true));
        Assert.True(y.Active);
        Assert.Equal(5_000, y.UntilMs);
        Assert.Equal(GestureYieldChange.None, y.Tick(4_999, trigger: false));
        Assert.Equal(GestureYieldChange.Exited, y.Tick(5_000, trigger: false));
        Assert.False(y.Active);
    }

    [Fact]
    public void A_still_true_trigger_renews_but_never_past_thirty_seconds_from_entry()
    {
        var y = new GestureYield();
        Assert.Equal(GestureYieldChange.Entered, y.Tick(0, trigger: true));
        Assert.Equal(GestureYieldChange.Renewed, y.Tick(3_000, trigger: true));
        Assert.Equal(7_000, y.UntilMs);
        // 续期只到「首次进入 + 30s」；不能无限往后推。
        Assert.Equal(GestureYieldChange.Renewed, y.Tick(29_500, trigger: true));
        Assert.Equal(GestureYieldRules.MaxDurationMs, y.UntilMs);
        Assert.Equal(GestureYieldChange.ForcedExit, y.Tick(30_000, trigger: true));
        Assert.False(y.Active);
    }

    [Fact]
    public void A_long_silence_after_the_last_renewal_still_exits_cleanly()
    {
        var y = new GestureYield();
        y.Tick(0, trigger: true);
        y.Tick(10_000, trigger: true);
        Assert.Equal(GestureYieldChange.None, y.Tick(13_999, trigger: false));
        Assert.Equal(GestureYieldChange.Exited, y.Tick(14_000, trigger: false));
    }

    [Fact]
    public void Manual_end_is_idempotent()
    {
        var y = new GestureYield();
        Assert.False(y.End());
        y.Tick(0, trigger: true);
        Assert.True(y.End());
        Assert.False(y.Active);
        Assert.False(y.End());
    }
}
