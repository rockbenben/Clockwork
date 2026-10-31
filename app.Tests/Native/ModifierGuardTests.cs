using System;
using Clockwork.Native;
using Xunit;

// 发键前那道闸的判定：哪些修饰键要从这次注入里摘出去。
//
// 这条判定的症状是「发出去的键被改了身份」——框里写的是回车，程序收到的是 Ctrl+回车，
// 用户完全看不懂（用户实报「发送 enter 还是无效」，2026-10-31）。
// 真进程里的自查（DevKeySend）验的是「目标程序收到什么」，单测在这儿钉的是**规则本身**。
public class ModifierGuardTests
{
    private const ushort LCTRL = 0xA2, RCTRL = 0xA3, LSHIFT = 0xA0, RSHIFT = 0xA1;
    private const ushort LALT = 0xA4, RALT = 0xA5, LWIN = 0x5B, RWIN = 0x5C;
    private const ushort CTRL = 0x11, SHIFT = 0x10, ALT = 0x12, WIN = 0x5B;

    // 组合里没有修饰键 → 按着的全是多余的，都得摘出去。
    [Fact]
    public void Stray_drops_everything_when_the_combo_has_no_modifier()
        => Assert.Equal(new ushort[] { LCTRL, RSHIFT },
                        ModifierGuard.StrayAmong(new ushort[] { LCTRL, RSHIFT }, Array.Empty<ushort>()));

    // 组合要 Ctrl → 左 Ctrl 与右 Ctrl 都算「要留着」（组合键写的是 Ctrl，不分左右）。
    [Fact]
    public void Stray_keeps_the_family_the_combo_wants()
        => Assert.Equal(new ushort[] { RSHIFT },
                        ModifierGuard.StrayAmong(new ushort[] { LCTRL, RSHIFT }, new ushort[] { CTRL }));

    // 组合里写死了具体那一个（左 Ctrl）也算要留着。
    [Fact]
    public void Stray_keeps_the_exact_key_the_combo_wants()
        => Assert.Empty(ModifierGuard.StrayAmong(new ushort[] { LCTRL }, new ushort[] { LCTRL }));

    [Fact]
    public void Stray_nothing_held_nothing_to_do()
        => Assert.Empty(ModifierGuard.StrayAmong(Array.Empty<ushort>(), Array.Empty<ushort>()));

    // Win 也要算进去：Win 键被别的组合带走时同样会让主键改了身份。
    [Fact]
    public void Stray_covers_win()
    {
        Assert.Equal(new ushort[] { LWIN }, ModifierGuard.StrayAmong(new ushort[] { LWIN }, Array.Empty<ushort>()));
        Assert.Empty(ModifierGuard.StrayAmong(new ushort[] { LWIN, RWIN }, new ushort[] { WIN }));
    }

    // Alt 同理（左右各一个，别只测 Ctrl）。
    [Fact]
    public void Stray_covers_alt()
    {
        Assert.Equal(new ushort[] { LALT }, ModifierGuard.StrayAmong(new ushort[] { LALT }, new ushort[] { CTRL }));
        Assert.Empty(ModifierGuard.StrayAmong(new ushort[] { RALT }, new ushort[] { ALT }));
    }

    // SendKeys 序列里的修饰键前缀：^ Ctrl、% Alt、+ Shift。
    [Fact]
    public void WantedFromSendKeys_reads_prefixes()
    {
        Assert.Equal(new ushort[] { CTRL }, ModifierGuard.WantedFromSendKeys("^a"));
        Assert.Equal(new ushort[] { CTRL, SHIFT }, ModifierGuard.WantedFromSendKeys("^+a"));
        Assert.Equal(new ushort[] { ALT }, ModifierGuard.WantedFromSendKeys("%{ENTER}"));
        Assert.Equal(new ushort[] { SHIFT }, ModifierGuard.WantedFromSendKeys("+{TAB}"));
    }

    // 花括号组与圆括号里的是**字面量**：「{+}」是加号、「(+)」是带括号的加号，都不是修饰键。
    [Fact]
    public void WantedFromSendKeys_ignores_literal_groups()
    {
        Assert.Empty(ModifierGuard.WantedFromSendKeys("{+}"));
        Assert.Empty(ModifierGuard.WantedFromSendKeys("(+)"));
        Assert.Empty(ModifierGuard.WantedFromSendKeys("{ENTER}"));
    }
}
