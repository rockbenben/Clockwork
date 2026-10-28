using System;
using System.Globalization;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// 托盘状态灯的拼装规则。符号刻意不走 resx（设计决定见 Core/TrayTip 头注），
// 所以这里钉的是**语义**：什么时候出现什么符号、过期勿扰不许上灯、setter 的 63 字上限。
// 让路那句例外——它必须说清「右键现在不归我们管」，符号表达不了，所以走 resx；
// 于是「63 字符装得下」这条判据只能用**每门语言的真值**来验（中性短句只有 8 字）。
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

    [Fact]
    public void Yielding_appends_its_short_label_and_still_fits_the_limit()
    {
        var s = TrayTip.Build(true, Now.AddHours(1), Now, "让路中·不抢右键");
        Assert.Contains("让路中·不抢右键", s);
        Assert.True(s.Length <= 63);
    }

    // 让路那句是**本地化**的：德语 46 字、印地语与荷兰语 47 字、俄语 41 字。
    // 三段状态同时在场（名字 + ▶ + 勿扰钟点 + 让路）必然越过 63，而硬切会在句中截断，
    // 德语会切成「… wird nicht erfa」这种半句话。这条按**每门语言的真值**跑，
    // 用中性短句去试是试不出来的——中文那句只有 8 字。
    [Fact]
    public void Every_language_yielding_label_survives_the_NotifyIcon_limit()
    {
        foreach (var (_, code) in Languages.All)
        {
            var label = Strings.TryGet("Gesture_WatchYielding", CultureInfo.GetCultureInfo(code));
            Assert.NotNull(label);   // 缺一条文案就是界面上的原始键名，另有一条测试盯那个
            var s = TrayTip.Build(running: true, dndUntil: Now.AddHours(1), now: Now, yielding: label!);
            Assert.True(s.Length <= 63, $"{code} 的让路文案把 tooltip 撑到 {s.Length} 字符");
            // 不能被截断：那句必须原样出现在 tooltip 里。
            Assert.Contains(label!, s);
        }
    }

    // 让路与勿扰钟点互斥：让路赢。丢掉的是钟点（「勿扰到几点恢复」），
    // 留下的是「右键现在不归我们管」——后者才是用户此刻真正要知道的。
    [Fact]
    public void Yielding_takes_the_tooltip_budget_over_the_dnd_clock()
    {
        var s = TrayTip.Build(true, Now.AddHours(1), Now, "让路中·不抢右键");
        Assert.Contains("让路中·不抢右键", s);
        Assert.DoesNotContain("23:00", s);
    }
}
