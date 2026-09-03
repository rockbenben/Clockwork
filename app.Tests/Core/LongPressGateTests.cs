using Clockwork.Core;
using Xunit;

// 「长按中键」的判定。这段逻辑的每一条分支都对应一件用户会立刻发现的事，
// 而其中最要命的一条是「普通中键点击还能不能用」——它坏了的表现是全系统中键失灵。
public class LongPressGateTests
{
    private const int Hold = 350;
    private static LongPressGate New() => new(Hold);

    // 按下的当口还不知道这是长按还是点击，只能先扣住。
    [Fact]
    public void Down_is_always_swallowed()
    {
        var g = New();
        Assert.Equal(PressVerdict.Swallow, g.OnMiddleDown(1000, 100, 100));
        Assert.True(g.Pending);
    }

    // 短按 = 普通中键点击：必须补发一次真的，否则中键在全系统失灵。
    [Fact]
    public void Short_press_replays_a_real_click()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        Assert.Equal(PressVerdict.ReplayClick, g.OnMiddleUp(1000 + Hold - 1));
        Assert.False(g.Pending);
    }

    // 按满时长就弹，不等抬起——闹钟问 PollFire，它说「就是现在」。
    [Fact]
    public void The_panel_fires_the_moment_the_hold_is_reached()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        Assert.False(g.PollFire(1000 + Hold - 1));   // 还差一毫秒：先别弹
        Assert.True(g.PollFire(1000 + Hold));        // 到点
        Assert.False(g.Pending);
    }

    // 只弹一次：闹钟可能被问第二遍，不能弹出两个面板。
    [Fact]
    public void PollFire_only_says_yes_once()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        Assert.True(g.PollFire(1000 + Hold));
        Assert.False(g.PollFire(1000 + Hold + 500));
    }

    // 弹过之后那次抬起必须吞掉——放行的话，指针正压在刚弹出来的面板上，
    // 那一下中键会把面板当场点掉。这是「到时间就弹」引入的新状态，也是它唯一的代价。
    [Fact]
    public void The_release_after_firing_is_swallowed_not_replayed()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        g.PollFire(1000 + Hold);
        Assert.Equal(PressVerdict.Swallow, g.OnMiddleUp(1000 + Hold + 800));
    }

    // 弹过之后还按着不动地挪鼠标：不该被当成拖拽去补发一次中键按下。
    [Fact]
    public void Moving_after_firing_just_passes_through()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        g.PollFire(1000 + Hold);
        Assert.Equal(PressVerdict.Pass, g.OnMove(400, 400));
    }

    // 没按着的时候问闹钟：一律说不该弹。
    [Fact]
    public void PollFire_says_no_when_nothing_is_held()
        => Assert.False(New().PollFire(99999));

    // 按住不动的小抖动不该被当成拖拽——手指按下去时鼠标动一两个像素是常态。
    [Fact]
    public void Tiny_jitter_while_held_is_not_a_drag()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        // 放行而不是吞掉：吞掉等于在低级钩子里拦下 WM_MOUSEMOVE，光标会被钉住不动，
        // 而且从此位移再也累加不到容差之上——自动滚动永远起不来（见 OnMove 的说明）。
        Assert.Equal(PressVerdict.Pass, g.OnMove(102, 103));      // 曼哈顿距离 5，容差 6
        Assert.True(g.Pending);
        Assert.True(g.PollFire(1000 + Hold));                     // 抖动没打断计时
    }

    // 按住期间的移动**一次都不能吞**：只要吞掉一次，光标就停在那儿，
    // 之后每一条移动消息都从同一个原点算起，位移永远追不上容差。
    [Fact]
    public void No_move_is_ever_swallowed()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        for (int d = 0; d <= 20; d++)
            Assert.NotEqual(PressVerdict.Swallow, g.OnMove(100 + d, 100));
    }

    // 按住后真的移动 = 中键拖拽（浏览器自动滚动）：把扣住的按下补发回去，此后不再拦。
    [Fact]
    public void Moving_while_held_rescues_the_drag()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        Assert.Equal(PressVerdict.ReplayDownThenPass, g.OnMove(140, 100));
        Assert.False(g.Pending);
        // 按下已经还给下游了，这次抬起必须原样放行——再补发一次就成了两次按下一次抬起。
        Assert.Equal(PressVerdict.Pass, g.OnMiddleUp(1000 + 5000));
    }

    // 拖拽救援之后的移动不该再被拦。
    [Fact]
    public void Moves_after_the_drag_rescue_pass_through()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        g.OnMove(140, 100);
        Assert.Equal(PressVerdict.Pass, g.OnMove(180, 100));
    }

    // 没有扣着按下时的事件一律放行：钩子看到的是全系统的鼠标事件，
    // 绝大多数与本功能无关，这条是最常走的那一支。
    [Fact]
    public void Events_without_a_pending_down_pass_through()
    {
        var g = New();
        Assert.Equal(PressVerdict.Pass, g.OnMove(500, 500));
        Assert.Equal(PressVerdict.Pass, g.OnMiddleUp(1000));
    }

    // 抬起丢了（被别的钩子吞掉 / 系统丢事件）之后又来一次按下：
    // 以新的为准重新起算，而不是留一个永远等不到抬起的旧状态——
    // 否则下一次抬起会拿几分钟前的时刻去比，必然算成长按。
    [Fact]
    public void A_second_down_restarts_the_timing()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        g.OnMiddleDown(9000, 100, 100);   // 抬起丢了
        Assert.Equal(PressVerdict.ReplayClick, g.OnMiddleUp(9000 + 10));   // 按新的起点算：短按
    }

    // 阈值配成 0 会让每一次普通点击都变成长按，中键就再也点不出来了——夹到下界。
    [Fact]
    public void An_absurd_threshold_is_clamped_so_clicking_still_works()
    {
        var g = new LongPressGate(0);
        g.OnMiddleDown(0, 0, 0);
        Assert.Equal(PressVerdict.ReplayClick, g.OnMiddleUp(10));   // 10ms 仍算点击
    }

    [Fact]
    public void Reset_drops_the_pending_press_without_replaying()
    {
        var g = New();
        g.OnMiddleDown(1000, 100, 100);
        g.Reset();
        Assert.False(g.Pending);
        Assert.Equal(PressVerdict.Pass, g.OnMiddleUp(1000 + Hold));
    }
}
