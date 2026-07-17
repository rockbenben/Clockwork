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

    [Theory]
    [InlineData(ModifierKeys.Alt, Key.F4)]        // 全局注册会让所有程序 Alt+F4 关不了窗
    [InlineData(ModifierKeys.Alt, Key.Tab)]
    [InlineData(ModifierKeys.Alt, Key.Escape)]
    [InlineData(ModifierKeys.Alt, Key.Space)]
    [InlineData(ModifierKeys.Control, Key.Escape)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.Escape)]   // 任务管理器
    public void BuildCombo_rejects_reserved_system_combos(ModifierKeys mods, Key key)
        => Assert.Null(HotkeyCapture.BuildCombo(mods, key));

    [Theory]
    [InlineData("Alt+F4", true)]
    [InlineData("alt+f4", true)]              // 大小写不敏感（配置手改）
    [InlineData("Alt + F4", true)]            // 内部空格：解析器认，保留判定也必须认
    [InlineData("Control+Escape", true)]      // Control 别名
    [InlineData("Ctrl+Esc", true)]            // Esc 别名
    [InlineData("Shift+Ctrl+Escape", true)]   // 修饰键乱序
    [InlineData("Ctrl+Shift+Escape", true)]
    [InlineData("Ctrl+Alt+F4", false)]        // 多修饰键组合不保留
    [InlineData("Ctrl+Alt+Q", false)]
    [InlineData("not-a-combo", false)]        // 解析不了 → 交由无效键路径
    [InlineData(null, false)]
    public void IsReserved_normalizes_like_the_registration_parser(string? combo, bool expected)
        => Assert.Equal(expected, HotkeyCapture.IsReserved(combo));   // 与注册同口径解析后比对，别名/空格/乱序绕不过

    [Fact]
    public void BuildCombo_still_accepts_reserved_main_key_with_more_modifiers()
        => Assert.Equal("Ctrl+Alt+F4", HotkeyCapture.BuildCombo(ModifierKeys.Control | ModifierKeys.Alt, Key.F4));

    // —— 捕捉状态机（设置页急停键与组编辑器共用） ——
    [Theory]
    [InlineData(Key.Tab, ModifierKeys.None)]                  // 裸 Tab：焦点导航
    [InlineData(Key.Tab, ModifierKeys.Shift)]                 // Shift+Tab：反向导航
    [InlineData(Key.Enter, ModifierKeys.None)]                // 裸 Enter：默认按钮
    public void Capture_passes_through_navigation_keys(Key key, ModifierKeys mods)
        => Assert.Equal(HotkeyCapture.CaptureAction.PassThrough, HotkeyCapture.ProcessCaptureKey(key, mods, out _));

    [Fact]
    public void Capture_esc_cancels()
        => Assert.Equal(HotkeyCapture.CaptureAction.Cancel, HotkeyCapture.ProcessCaptureKey(Key.Escape, ModifierKeys.None, out _));

    [Theory]
    [InlineData(Key.Delete)]
    [InlineData(Key.Back)]
    public void Capture_bare_delete_clears(Key key)
        => Assert.Equal(HotkeyCapture.CaptureAction.Clear, HotkeyCapture.ProcessCaptureKey(key, ModifierKeys.None, out _));

    [Fact]
    public void Capture_modified_delete_is_a_combo_not_clear()   // Ctrl+Delete 是用户想录的组合，不能当清空吞掉
    {
        var act = HotkeyCapture.ProcessCaptureKey(Key.Delete, ModifierKeys.Control, out var combo);
        Assert.Equal(HotkeyCapture.CaptureAction.Captured, act);
        Assert.Equal("Ctrl+Delete", combo);
    }

    [Fact]
    public void Capture_valid_combo_is_captured()
    {
        var act = HotkeyCapture.ProcessCaptureKey(Key.F, ModifierKeys.Control | ModifierKeys.Alt, out var combo);
        Assert.Equal(HotkeyCapture.CaptureAction.Captured, act);
        Assert.Equal("Ctrl+Alt+F", combo);
    }

    [Theory]
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]   // 只按修饰键：继续等
    [InlineData(Key.Q, ModifierKeys.None)]             // 裸键组不出可注册组合：忽略
    [InlineData(Key.F4, ModifierKeys.Alt)]             // Alt+F4 保留组合：忽略而非捕获
    public void Capture_ignores_incomplete_or_reserved(Key key, ModifierKeys mods)
        => Assert.Equal(HotkeyCapture.CaptureAction.Ignore, HotkeyCapture.ProcessCaptureKey(key, mods, out _));
}
