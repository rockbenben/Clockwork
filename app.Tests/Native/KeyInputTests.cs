using Clockwork.Native;
using Xunit;

public class KeyInputTests
{
    [Theory]
    [InlineData("A", 65)]      // Keys.A
    [InlineData("5", 53)]      // D5
    [InlineData("F4", 115)]    // Keys.F4
    [InlineData("Enter", 13)]  // Keys.Enter
    [InlineData("esc", 27)]    // 别名→Escape
    [InlineData("10", 0)]      // 多位数字拒绝
    [InlineData("", 0)]
    public void KeysVk_maps(string key, int vk) => Assert.Equal(vk, KeyInput.KeysVk(key));

    [Fact]
    public void HotkeyParams_ctrl_alt_f12()
    {
        var p = KeyInput.ToHotkeyParams("Ctrl+Alt+F12");
        Assert.NotNull(p);
        Assert.Equal(2u | 1u, p!.Modifiers);   // Ctrl|Alt
        Assert.Equal((uint)123, p.Vk);          // Keys.F12
    }

    [Fact]
    public void HotkeyParams_ctrl_alt_q()   // 新默认急停键：确保字母键正确解析、可注册
    {
        var p = KeyInput.ToHotkeyParams("Ctrl+Alt+Q");
        Assert.NotNull(p);
        Assert.Equal(2u | 1u, p!.Modifiers);   // Ctrl|Alt
        Assert.Equal((uint)0x51, p.Vk);         // 'Q'
    }

    [Fact]
    public void HotkeyParams_null_when_no_key()
        => Assert.Null(KeyInput.ToHotkeyParams("Ctrl+Alt"));

    [Fact]
    public void HotkeyParams_win_bit()
        => Assert.Equal(8u, KeyInput.ToHotkeyParams("Win+D")!.Modifiers);
}
