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

    // ── 命中的是**哪一条** ──
    //
    // 笔迹「亮了」只说明认出了**形状**，不说明认出了**哪一个动作**。而手势是分应用的——
    // 同一个 ↑ 在浏览器里是「搜索」、在编辑器里是「复制」，于是「亮了」本身是有歧义的。
    // 笔迹旁要报「这将是哪件事」，就得知道命中的是哪一条，而 bool 把这个信息丢掉了。
    //
    // 这一族用的是**解析版**构造器（回调返回命中的那一步）。只答「匹不匹配」的那一档
    // 本来就不知道是哪一条，如实答 null——那不是缺陷，是那一档的能力边界。

    private static LaunchStep Step(string gesture) => new() { Gesture = gesture, Label = gesture };

    [Fact]
    public void The_matched_step_is_reported()
    {
        var hit = Step("D");
        var g = new GestureGate((p, _) => p == "D" ? hit : null, MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        Assert.True(g.LiveMatch());
        Assert.Same(hit, g.LiveStep);
    }

    // 没命中的时候这一格是空的——它答的是「哪一条」，不是「有没有」。
    [Fact]
    public void No_step_is_reported_when_nothing_matches()
    {
        var g = new GestureGate((p, _) => (LaunchStep?)null, MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        Assert.False(g.LiveMatch());
        Assert.Null(g.LiveStep);
    }

    // 继续画会换成另一条：这一格描述的是**此刻这一串**的命中结果。
    [Fact]
    public void The_reported_step_follows_the_shape_as_it_grows()
    {
        var up = Step("U");
        var upDown = Step("UD");
        var g = new GestureGate((p, _) => p == "U" ? up : p == "UD" ? upDown : null, MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 40; i++) g.OnMove(500, 500 - i * 5);   // 只画了 ↑
        Assert.True(g.LiveMatch());
        Assert.Same(up, g.LiveStep);
        for (int i = 1; i <= 30; i++) g.OnMove(502, 300 + i * 5);   // 补上 ↓
        Assert.True(g.LiveMatch());
        Assert.Same(upDown, g.LiveStep);
    }

    // 新的一笔开始时上一笔的结论必须作废：它描述的是「刚画的那一笔」，
    // 活得比那一笔久就会骗人（松手判定读的是此刻的形状，而这一格报的是上一笔的动作）。
    [Fact]
    public void A_new_stroke_clears_the_reported_step()
    {
        var hit = Step("D");
        var g = new GestureGate((p, _) => p == "D" ? hit : null, MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        Assert.True(g.LiveMatch());
        Assert.Same(hit, g.LiveStep);
        g.OnRightDown(500, 500);
        Assert.Null(g.LiveStep);
    }

    // 只答「匹不匹配」的那一档：这一格如实为空，而不是去猜一条。
    [Fact]
    public void The_bool_constructor_reports_no_step()
    {
        var g = new GestureGate(p => p == "D", MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        Assert.True(g.LiveMatch());
        Assert.Null(g.LiveStep);
    }
}
