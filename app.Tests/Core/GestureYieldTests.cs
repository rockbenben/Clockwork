using System;
using System.IO;
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

    // **用户按过「立即恢复手势」之后，判据还在也不自动再进。**
    //
    // 这一格以前住在协调器里，而协调器在换钩子/自愈重装时把它清掉了——
    // 于是用户点完不到一秒，链还堵着（判据仍在）就又进了让路，
    // 那个按钮等于没按过。文档里写的正是「在链真正疏通之前，它不会再自动让路」。
    //
    // 现在压制住在 GestureYield 里，只由**判据自己消失**来解除：
    // 钩子生命周期的事件（换钩子、自愈重装、手动重挂）碰不到它。
    //
    // **顺序要紧**：必须先 Suppress() 再 End()——那正是协调器里的顺序
    // （EndGestureYield：Suppress → End）。反过来写的话，「End() 顺手把压制也清了」
    // 那种改法这条测试照样绿：End() 在压制之前调用，清掉的是个空的标记。
    // 而 End() 清掉压制恰恰就是原来的 bug 形状（协调器的 ClearGestureYield 就是调 End()）。
    //
    // 让它变红的改动：给 End() 加一句「也清掉压制」，或者把 ClearGestureYield 改成清压制。
    [Fact]
    public void A_manual_suppression_survives_until_the_trigger_clears()
    {
        var y = new GestureYield();
        y.Tick(0, trigger: true);
        y.Suppress();
        y.End();                                   // 协调器那一档的顺序：先压住，再结束

        Assert.True(y.Suppressed);                 // **End() 不许碰压制**
        Assert.False(y.Active);

        // 判据还在 → 不进
        Assert.Equal(GestureYieldChange.None, y.Tick(1_000, trigger: true));
        Assert.False(y.Active);

        // 判据消失 → 压制作废
        Assert.Equal(GestureYieldChange.None, y.Tick(2_000, trigger: false));
        Assert.False(y.Suppressed);

        // 于是下一次判据又成立时就能进了
        Assert.Equal(GestureYieldChange.Entered, y.Tick(3_000, trigger: true));
        Assert.True(y.Active);
    }

    // **30 秒上限解除后也一样**：上限强制解除是一次真正的恢复，不是下一拍又进。
    // 这一格由状态机自己落（见 GestureYield.Tick 的 ForcedExit 那一档），
    // 所以协调器换钩子的时候碰不到它。
    [Fact]
    public void A_forced_exit_suppresses_the_next_entry()
    {
        var y = new GestureYield();
        Assert.Equal(GestureYieldChange.Entered, y.Tick(0, trigger: true));
        Assert.Equal(GestureYieldChange.ForcedExit, y.Tick(GestureYieldRules.MaxDurationMs, trigger: true));
        Assert.True(y.Suppressed);

        Assert.Equal(GestureYieldChange.None, y.Tick(GestureYieldRules.MaxDurationMs + 1, trigger: true));
        Assert.False(y.Active);
    }

    // **「立即恢复手势」那一档必须真的压住自动让路。**
    //
    // 压制住在状态机里（GestureYield.Suppress），而**只有协调器会去设它**——
    // 它要是漏了这一句，用户点完按钮不到一秒（判据还在）就又进让路了，
    // 那个按钮等于没按过。而状态机那几条测试全是直接调 GestureYield，
    // 根本碰不到协调器这一段。
    //
    // 让它变红的改动：把 EndGestureYield 里那句 _yield.Suppress() 删掉（或挪到 End 之后）。
    [Fact]
    public void The_manual_resume_actually_suppresses_the_next_auto_entry()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Services", "GestureCoordinator.cs"));
        int i = src.IndexOf("public void EndGestureYield()", StringComparison.Ordinal);
        Assert.True(i > 0, "找不到 EndGestureYield");
        int end = src.IndexOf("public void EndGestureYield()", i + 1, StringComparison.Ordinal);
        if (end < 0) end = src.Length;
        var body = src.Substring(i, end - i);

        Assert.Contains("_yield.Suppress()", body);
        // **压制要落在结束之前**：反过来的话，End() 之后标记就没了。
        int suppress = body.IndexOf("_yield.Suppress()", StringComparison.Ordinal);
        int endCall = body.IndexOf("_yield.End()", StringComparison.Ordinal);
        Assert.True(suppress >= 0 && endCall >= 0, "EndGestureYield 里要有压制与结束两句");
        Assert.True(suppress < endCall, "压制必须落在结束之前");
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!.FullName;
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
