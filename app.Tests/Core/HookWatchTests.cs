using Clockwork.Core;
using Xunit;

// 钩子自愈的三条守卫。抽成纯函数就是为了这个：任何一条被删掉、被挪错、被改成 <=，
// 都得有测试变红——否则下次有人手滑把下限删了，症状是「装上几秒就被摘掉的钩子开始几秒一次地
// 循环摘装」，那在生产上看不出来毛病，直到某一次正好压掉用户的中键长按。
//
// 判据是「同一个采样区间对齐」：区间内光标动过（物理移动发生了），活着的钩子心跳必然也在
// 这同一个区间内（silentSinceMs ≤ 实测区间长度）。区间里没动鼠标则根本没有信息，不许动手。
public class HookWatchTests
{
    private const long SampleMs = 500;    // 生产采样间隔（CursorWatchTick）
    private const long SkewMs = 250;      // 生产值 HealBeatSkewMs
    private const long GapMs = 3000;      // 生产值 HealMinGapMs
    private const long FreshMs = 400;     // 区间内有心跳：活钩的正常读数（< SampleMs）
    private const long LongAgo = long.MaxValue;

    // 区间内光标动了、心跳却比「区间 + 余量」还老、距上次摘钩也够久 → 摘钩重装。
    // 这一条同时钉死了「心跳用严格大于」「下限用大于等于」的口径。
    [Fact]
    public void Heals_when_cursor_moved_but_beat_predates_the_sample_window()
        => Assert.True(HookWatch.ShouldHeal(true, SampleMs + SkewMs + 1, SampleMs, GapMs, SkewMs, GapMs));

    // 「从没自愈过」由调用方把 _lastHealAt 的 0 哨兵换成 long.MaxValue 传进来，必须放行。
    // 这一条被卡住的话，第一次真故障就没人管了——下限的意义是别循环，不是别自愈。
    [Fact]
    public void First_ever_heal_is_not_blocked_by_the_gap()
        => Assert.True(HookWatch.ShouldHeal(true, LongAgo, SampleMs, LongAgo, SkewMs, GapMs));

    // **核心回归**：用户停手时本区间光标没动，哪怕心跳已经老到天荒地老也不许重装——
    // 没有输入就没有事件，那时「沉默」是正常的。旧版两扇错位的时间窗（活动 2000ms /
    // 沉默 1500ms）就栽在这一格：移动后停手 1.5~2 秒必误判，实测一天刷了 518 行 reinstall。
    [Fact]
    public void Does_not_heal_when_cursor_did_not_move_in_the_sample()
        => Assert.False(HookWatch.ShouldHeal(false, LongAgo, SampleMs, LongAgo, SkewMs, GapMs));

    // 光标动了、心跳也新：钩子活着，别动它。
    [Fact]
    public void Does_not_heal_when_beat_is_fresh_in_the_same_sample()
        => Assert.False(HookWatch.ShouldHeal(true, FreshMs, SampleMs, LongAgo, SkewMs, GapMs));

    // 正好卡在「区间 + 余量」上不算死，判定是严格大于。取等号的话，采样计时器与心跳都有
    // 几十毫秒抖动，会在钩子恰好还活着的那一刻动手。
    [Fact]
    public void Exactly_at_the_threshold_is_still_alive()
        => Assert.False(HookWatch.ShouldHeal(true, SampleMs + SkewMs, SampleMs, LongAgo, SkewMs, GapMs));

    // 系统卡顿时实测区间自己变长，新鲜度的界跟着长：区间花了 1200ms、心跳 1100ms 老
    //（移动确实发生在这个被拉长的区间里），不许因为名义上是 500ms 就摘钩。
    [Fact]
    public void A_long_sample_window_extends_the_freshness_bound()
        => Assert.False(HookWatch.ShouldHeal(true, 1100, 1200, LongAgo, SkewMs, GapMs));

    // 反方向也要钉住：区间拉长了但心跳比区间还老，一样是死了（余量之外无宽宥）。
    [Fact]
    public void A_long_sample_window_still_heals_when_beat_predates_it()
        => Assert.True(HookWatch.ShouldHeal(true, 1500, 1200, LongAgo, SkewMs, GapMs));

    // 刚摘过一次别马上再来：摘装之间有个输入空窗，正好可能压掉一次中键按下。
    [Fact]
    public void Does_not_heal_inside_the_min_gap()
        => Assert.False(HookWatch.ShouldHeal(true, SampleMs + SkewMs + 1, SampleMs, GapMs - 1, SkewMs, GapMs));

    // 下限到点即放行：是「至少间隔这么久」，不是「超过这么久」。
    [Fact]
    public void Heals_the_moment_the_gap_elapses()
        => Assert.True(HookWatch.ShouldHeal(true, SampleMs + SkewMs + 1, SampleMs, GapMs, SkewMs, GapMs));
}
