using System;
using Clockwork.Core;
using Xunit;

// 托盘状态灯的拼装规则。符号刻意不走 resx（设计决定见 Core/TrayTip 头注），
// 所以这里钉的是**语义**：什么时候出现什么符号、过期勿扰不许上灯、setter 的 63 字上限。
public class TrayTipTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 21, 0, 0);

    [Fact]
    public void Idle_shows_bare_name()
        => Assert.Equal("Clockwork", TrayTip.Build(running: false, dndUntil: null, Now));

    [Fact]
    public void Running_gets_the_play_mark()
    {
        var s = TrayTip.Build(running: true, dndUntil: null, Now);
        Assert.Equal("Clockwork  \u25B6", s);
    }

    [Fact]
    public void Dnd_shows_the_resume_clock()
    {
        var s = TrayTip.Build(false, Now.AddMinutes(40), Now);
        Assert.Equal("Clockwork  \u23F8 21:40", s);
    }

    [Fact]
    public void Both_states_stack()
    {
        var s = TrayTip.Build(true, Now.AddHours(2), Now);
        Assert.Contains("\u25B6", s);
        Assert.Contains("23:00", s);
    }

    // 到点与「拿着过期时刻调用」都不许亮勿扰灯——恢复是无声的，灯比状态先知道就成谎话。
    [Fact]
    public void Expired_dnd_never_shows()
    {
        Assert.Equal("Clockwork", TrayTip.Build(false, Now.AddSeconds(-1), Now));
        Assert.DoesNotContain("\u23F8", TrayTip.Build(true, Now.AddDays(-1), Now));
    }

    [Fact]
    public void Never_exceeds_the_NotifyIcon_text_limit()
    {
        // 现实串远短于 63；上限是 setter 契约，越界即 ArgumentException，测试替 shell 守门。
        Assert.True(TrayTip.Build(true, new DateTime(9999, 12, 31, 23, 59, 59), Now).Length <= 63);
    }
}
