using System.Windows.Input;
using Clockwork.Native;
using Xunit;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.Q, "Ctrl+Alt+Q")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F12, "Ctrl+Alt+Shift+F12")]
    [InlineData(ModifierKeys.Control, Key.D1, "Ctrl+1")]                 // 数字显示 1 而非 D1
    [InlineData(ModifierKeys.Control, Key.OemSemicolon, "Ctrl+Oem1")]   // Oem 符号(WPF 名 Oem1)：之前被窄白名单吞掉，现已接受且可注册
    [InlineData(ModifierKeys.Alt, Key.OemPlus, "Alt+OemPlus")]
    public void BuildCombo_accepts_registrable_combos(ModifierKeys mods, Key key, string expected)
        => Assert.Equal(expected, HotkeyCapture.BuildCombo(mods, key));

    [Fact]
    public void BuildCombo_requires_a_modifier()
        => Assert.Null(HotkeyCapture.BuildCombo(ModifierKeys.None, Key.Q));   // 裸键不接受（防全局劫键）

    [Fact]
    public void BuildCombo_rejects_modifier_only()
        => Assert.Null(HotkeyCapture.BuildCombo(ModifierKeys.Control, Key.LeftShift));

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.System)]
    public void IsModifierKey_true_for_modifiers(Key k) => Assert.True(HotkeyCapture.IsModifierKey(k));

    [Fact]
    public void IsModifierKey_false_for_letter() => Assert.False(HotkeyCapture.IsModifierKey(Key.Q));
}
