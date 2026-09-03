using Clockwork.Core;
using Xunit;

// **LiveMatch 跑在低级鼠标钩子那条线程上，而那条线程有 300ms 的硬预算。**
//
// Windows 的 LowLevelHooksTimeout（默认 300ms）：回调超时系统就**静默**把钩子踢掉，
// 之后既没有回调也没有报错，表现为「手势和中键长按用了一会儿突然全没了，重启才好」，
// 而任何日志里都不会有一个字（MouseHook 类头第 2 条写着这件事）。
// 所以这里的开销不是「性能问题」，它是一条正确性边界。
//
// **而兜住这条边界的不是复杂度，是 MaxPoints 那道夹子。** 实测（这台机器，最坏形状：
// 每个采样点都恰好拐一次，于是 legs ≈ points，把 Analyze 那个 O(legs²) 的内层顶到最大）：
//
//     128 点  0.073 ms      512 点  1.13 ms
//     256 点  0.224 ms     1024 点  5.28 ms        每翻一倍 ≈ 4.7–5×
//
// 也就是说它**确实是二次的**，而这没问题：采样点被夹在 1024，最坏 5.3ms 对 300ms 预算
// 还有约 57 倍余量。夹子一旦被调回 4096，同一形状实测单次 85ms——余量掉到 3.5 倍，
// 再叠上这条线程上别的活（笔迹投递、心跳）和比这台慢几倍的机器就会越界。
//
// **所以这里断言的是采样点数，不是毫秒。** 上一版断言 `perCallMs < 30`，在这台空载机器上
// 稳过（余量 5.7 倍），但并发跑多个测试套件时实测 82.4 / 70.7 ms——6 次里红 5 次。
// 而 CI 的 release job 是 `needs: test`，于是一条噪声就卡住发布。绝对毫秒界没法在未知硬件上
// 断言：它同时在量两件事（算法有没有退化、这台机器快不快），而只有前者是缺陷。
// 「夹子被调大」这件事计数断言抓得**更准**——不受负载影响，也不用等到某台机器刚好越界。
public class GestureGatePerfTests
{
    // 4K 主屏那一档：minLegPx = 3840*25/1000 = 96，采样间隔 _stepPx = 24。
    private const int MinLegPx = 96, StepPx = 24;

    // 之字形：每个采样点都换方向，legs 拿到理论上限。
    private static GestureGate Zigzag(int moves)
    {
        var g = new GestureGate(_ => false, MinLegPx);
        g.OnRightDown(0, 0);
        for (int i = 1; i <= moves; i++)
            g.OnMove(i * StepPx, (i % 2) * StepPx * 2);
        return g;
    }

    // 真正的守卫：无论来多少次移动，缓冲区都停在 MaxPoints（1024，含按下那一点）。
    // 那个常量被调大、或 OnMove 里那句 `if (_pts.Count < MaxPoints)` 被删，这条立刻红。
    // 三档都实测过，都是 1024——按住右键乱画一分钟也不会更多。
    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(65536)]
    public void The_sample_buffer_is_capped(int moves)
        => Assert.Equal(1024, Zigzag(moves).PointCount);

    // 一笔**真手势**为什么便宜：腿数差两个数量级，而内层是 O(legs²)。
    // 断言腿数而不是毫秒——那才是「便宜」的机制，且与机器无关。
    // （最坏形状 1024 个采样点是 1024 条腿；下面这一笔 121 个采样点只有 2 条。）
    //
    // 腿数要在**收笔之后**才读得到：LiveMatch 只回答「此刻像不像某条已配的手势」，
    // 不写 Path——实测画完两条腿时 Path 仍是空串，OnRightUp 之后才变成 "DR"。
    // 拿 LiveMatch 之后的 Path 去断言会得到一条永远为空、因而永远绿的用例。
    [Fact]
    public void A_realistic_two_leg_gesture_stays_at_two_legs()
    {
        var g = new GestureGate(_ => false, MinLegPx);
        g.OnRightDown(0, 0);
        // ↓ 再 → ：各 60 个采样点，合起来比任何人真画的都长一些。
        for (int i = 1; i <= 60; i++) { g.OnMove(0, i * StepPx); g.LiveMatch(); }
        for (int i = 1; i <= 60; i++) { g.OnMove(i * StepPx, 60 * StepPx); g.LiveMatch(); }
        Assert.Equal(121, g.PointCount);
        g.OnRightUp();
        Assert.Equal("DR", g.Path);   // 2 条腿，而最坏形状是 1024 条
    }
}
