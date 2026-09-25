using Clockwork.Core;
using Xunit;

// LiveMatch 节流的裁决。它跑在低级钩子线程上（300ms 静默预算），节流砍的是
// 「每个新采样点全量重跑一次 Analyze」的 O(点²) 累积（实测 1024 点单次 5.28ms）。
// 断言全部落在**计算次数**上而不是毫秒——绝对时长在未知硬件与并发负载下站不住
//（GestureGatePerfTests 的毫秒界红过 6 次里 5 次，教训在案），而次数是确定的。
public class LiveMatchThrottleTests
{
    // 一条命中 "R" 的直划。stepPx = minLeg/4 = 10，每 OnMove 一点都进采样。
    private static GestureGate Gate(int every) =>
        new((p, _) => p == "R", minLegPx: 40, liveEveryPoints: every);

    private static void StrokeRight(GestureGate g, int points)
    {
        g.OnRightDown(0, 0);
        for (int i = 1; i <= points; i++) { g.OnMove(i * 10, 0); g.LiveMatch(); }
    }

    [Fact]
    public void Default_gate_computes_every_call()
    {
        var g = Gate(every: 0);
        StrokeRight(g, 10);
        // 不节流 = 旧语义：每点一算。起点 1 个采样，第 1 个 move 起 count≥2 全够格 → 10 次全算。
        Assert.Equal(10, g.LiveComputations);
    }

    [Fact]
    public void Throttled_gate_recomputes_once_per_interval()
    {
        var g = Gate(every: 8);
        StrokeRight(g, 40);
        // 40 次问话：count=2 真算一次，此后每满 8 个新点再算（2/10/18/26/34）——5 次，不是 40 次。
        Assert.InRange(g.LiveComputations, 4, 6);
        Assert.True(g.LiveComputations < 10, $"节流没生效：{g.LiveComputations} 次真算");
    }

    // 节流只许拖慢变色，不许说错话：在**每个真算点**上，节流版的答案必须与不节流版
    // 同一时刻一致。缓存窗口里返回刚过期一点的旧结论是设计本意（变色最迟慢 8 个点），
    // 所以只在 LiveComputations 跳了一格的那些点上对质。
    [Fact]
    public void Every_fresh_computation_agrees_with_the_unthrottled_gate()
    {
        var free = Gate(every: 0);
        var lazy = Gate(every: 8);
        free.OnRightDown(0, 0); lazy.OnRightDown(0, 0);
        for (int i = 1; i <= 30; i++)
        {
            free.OnMove(i * 10, 0); lazy.OnMove(i * 10, 0);
            bool a = free.LiveMatch();
            int before = lazy.LiveComputations;
            bool b = lazy.LiveMatch();
            bool recomputed = lazy.LiveComputations > before;
            Assert.False(recomputed && a != b,
                $"点 {i}：节流版此刻真算了，却给出不一致的答案（不节流={a} 节流={b}）");
        }
        Assert.True(free.LiveMatch());     // 30 点的 → 早已命中 "R"
        Assert.True(lazy.LiveMatch());     // 节流版最迟在窗口尽头跟上：变色可迟，不可反向地迟
    }

    // 上一笔的缓存绝不能漏到下一笔（与 Path 必须随按下清账同一条规矩）。
    [Fact]
    public void New_stroke_invalidates_last_stroke_answer()
    {
        var g = Gate(every: 8);
        StrokeRight(g, 30);
        Assert.True(g.LiveMatch());                    // "R" 命中
        g.OnRightUp();
        g.OnRightDown(500, 500);                       // 新一笔起笔
        g.OnMove(500, 508); g.OnMove(500, 516);        // 两个短点，尚未构成任何方向
        Assert.False(g.LiveMatch());                   // 若拿上一笔的 true 糊弄，这里就红了
        g.Reset();
        g.OnRightDown(9, 9);
        g.OnMove(9, 90); g.OnMove(9, 120);             // Reset 之后同样不许吃旧缓存
        Assert.False(g.OnRightUp() is PressVerdict.Fire);
    }
}
