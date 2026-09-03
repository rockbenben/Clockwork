using Clockwork.Core;
using Xunit;

// 边画边认：笔迹在命中的那一刻变色，松手之前你就知道认没认出来。
// Quicker 的手册把这一条写在触发说明里（「识别到手势后（线条变色）松开鼠标即可立即触发」），
// 而我们从前是彻底沉默到抬起——画错了要松手才知道。
public class LiveMatchTests
{
    private const int MinLeg = 40;

    // 还没画出东西时不该亮。
    [Fact]
    public void Nothing_drawn_is_not_a_match()
    {
        var g = new GestureGate(_ => true, MinLeg);
        Assert.False(g.LiveMatch());          // 连按下都没有
        g.OnRightDown(500, 500);
        Assert.False(g.LiveMatch());          // 只有起点一个采样点
    }

    // 画够一条腿、且命中配置时亮起。
    [Fact]
    public void It_lights_up_when_the_stroke_matches()
    {
        var g = new GestureGate(p => p == "D", MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        Assert.True(g.LiveMatch());
    }

    // **画着画着会灭掉。** 命中是「此刻这一串」的属性，不是一旦点亮就锁住的状态：
    // 只绑了 ↑ 的话，画出 ↑ 时亮，继续往下画成 ↑↓ 就该灭——否则松手会跑一个你已经不想要的动作。
    [Fact]
    public void It_goes_out_when_the_stroke_grows_past_the_match()
    {
        var g = new GestureGate(p => p == "U", MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 40; i++) g.OnMove(500, 500 - i * 5);   // ↑ 200px
        Assert.True(g.LiveMatch());
        for (int i = 1; i <= 30; i++) g.OnMove(502, 300 + i * 5);   // 折返 150px，已成 ↑↓
        Assert.False(g.LiveMatch());
    }

    // 反过来：画到一半还不算数，补齐第二笔才亮。
    [Fact]
    public void It_lights_up_only_once_the_second_leg_is_there()
    {
        var g = new GestureGate(p => p == "UD", MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 40; i++) g.OnMove(500, 500 - i * 5);   // 只画了 ↑
        Assert.False(g.LiveMatch());
        for (int i = 1; i <= 30; i++) g.OnMove(502, 300 + i * 5);   // 补上 ↓
        Assert.True(g.LiveMatch());
    }

    // 匹配不上的串不该亮——否则「变色」这个信号就不值钱了。
    [Fact]
    public void An_unbound_stroke_stays_dark()
    {
        var g = new GestureGate(p => p == "RD", MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 40; i++) g.OnMove(500 + i * 5, 500);   // 只画了 R
        Assert.False(g.LiveMatch());
    }

    // 抬起之后不再算数（笔迹也收了）。
    [Fact]
    public void After_release_there_is_nothing_to_light()
    {
        var g = new GestureGate(p => p == "D", MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        g.OnRightUp();
        Assert.False(g.LiveMatch());
    }
}
