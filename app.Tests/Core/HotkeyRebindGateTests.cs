using System.Collections.Generic;
using Clockwork.Core;
using Xunit;

// HotkeyRebindGate 的承重分支：脏检查省的是全量重绑，但不能顺手把「失败重试」的隐式通道掐掉。
// 这四条正是当初最容易被「简化成签名没变就 return」改错的地方。
public class HotkeyRebindGateTests
{
    private static string Sig(params (string id, string hotkey)[] gs)
        => HotkeyRebindGate.Signature(gs);

    [Fact]
    public void Unchanged_and_fully_registered_skips()
    {
        var s = Sig(("g1", "Ctrl+Alt+1"), ("g2", "Ctrl+Alt+2"));
        Assert.True(HotkeyRebindGate.ShouldSkip(s, s, failCount: 0, registeredCount: 2));
    }

    // 别的程序放开了某个键 → 下一轮必须靠重绑抢回来：有失败项就不许跳。
    [Fact]
    public void A_pending_failure_forces_a_rebind_even_when_the_signature_matches()
        => Assert.False(HotkeyRebindGate.ShouldSkip("x", "x", failCount: 1, registeredCount: 3));

    // 启动首轮还没有过一次成功绑定（prevSig 为空）——没有「上一轮」可比较，必须绑。
    [Fact]
    public void First_bind_never_skips()
        => Assert.False(HotkeyRebindGate.ShouldSkip("", "", failCount: 0, registeredCount: 0));

    // 录键挂起把注册全注销了（registeredCount 归零）：恢复时必须重建，
    // 不能被「签名与挂起前一样」挡回去——热键会从此静默失灵。
    [Fact]
    public void Cleared_bindings_force_a_rebind()
        => Assert.False(HotkeyRebindGate.ShouldSkip("x", "x", failCount: 0, registeredCount: 0));

    // 配置任何一处影响绑定的改动都要翻签名：改键、启停（决定该组进不进签名）、增删组。
    [Fact]
    public void Signature_chips_follow_every_binding_relevant_change()
    {
        var before = Sig(("g1", "Ctrl+Alt+1"), ("g2", "Ctrl+Alt+2"));
        Assert.NotEqual(before, Sig(("g1", "Ctrl+Alt+1"), ("g2", "Ctrl+Alt+9")));   // 改键
        Assert.NotEqual(before, Sig(("g2", "Ctrl+Alt+2")));                          // 禁用 g1（调用方过滤后少一项）
        Assert.NotEqual(before, Sig(("g2", "Ctrl+Alt+2"), ("g1", "Ctrl+Alt+1")));    // 换序：争用同一组合时谁赢会变
        Assert.Equal(before, Sig(("g1", "Ctrl+Alt+1"), ("g2", "Ctrl+Alt+2")));       // 一字未动
    }

    // 没有组绑热键的常态（很常见：多数人不用组热键）：签名恒为空串，ShouldSkip 因
    // registeredCount==0 永远走重建——但那是零注册的空转遍历，代价可忽略；
    // 钉住它是为了别有人往「prevSig 空也跳」的方向改，那会弄断首绑。
    [Fact]
    public void No_bound_groups_rebuilds_an_emptily_not_skips_wrongly()
    {
        var empty = Sig();
        Assert.False(HotkeyRebindGate.ShouldSkip(empty, empty, 0, 0));
    }
}
