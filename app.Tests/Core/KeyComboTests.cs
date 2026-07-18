using Clockwork.Core;
using Xunit;

public class KeyComboTests
{
    [Fact]
    public void Parse_ctrl_alt_key()
    {
        var p = KeyCombo.ParseCombo("Ctrl+Alt+K");
        Assert.Contains("Ctrl", p.Modifiers);
        Assert.Contains("Alt", p.Modifiers);
        Assert.Equal("K", p.Key);
        Assert.False(p.UseWin);
    }

    [Fact]
    public void Parse_win_d()
    {
        var p = KeyCombo.ParseCombo("Win+D");
        Assert.True(p.UseWin);
        Assert.Equal("D", p.Key);
    }

    [Fact]
    public void Parse_control_alias_to_ctrl()
        => Assert.Contains("Ctrl", KeyCombo.ParseCombo("control+c").Modifiers);

    [Fact]
    public void SendKeysString_alt_k_lowercases()   // Alt+K → %k（大写会变 Alt+Shift+K）
        => Assert.Equal("%k", KeyCombo.ToSendKeysString(KeyCombo.ParseCombo("Alt+K")));

    [Fact]
    public void SendKeysString_f4_braces()
        => Assert.Equal("%{F4}", KeyCombo.ToSendKeysString(KeyCombo.ParseCombo("Alt+F4")));

    [Fact]
    public void SendKeysString_named_enter()
        => Assert.Equal("^{ENTER}", KeyCombo.ToSendKeysString(KeyCombo.ParseCombo("Ctrl+Enter")));

    [Theory]
    [InlineData("Backspace", true)]   // 捕获给的 WPF 名（Back→别名 Backspace 或原名）须能编码，否则捕获对话框看似无响应
    [InlineData("Back", true)]
    [InlineData("CapsLock", true)]
    [InlineData("F13", true)]         // SendKeys 支持到 F16
    [InlineData("F16", true)]
    [InlineData("PageDown", true)]
    [InlineData("Ctrl+Enter", true)]
    [InlineData("Win+E", false)]      // SendKeys 不支持 Win
    [InlineData("OemComma", false)]   // 编码不了 → 捕获拒收（否则运行时被当字面文本打进目标窗口）
    [InlineData("F17", false)]
    public void CanEncodeForSendKeys_matches_sendkeys_support(string combo, bool ok)
        => Assert.Equal(ok, KeyCombo.CanEncodeForSendKeys(combo));

    [Fact]
    public void SendKeysSequence_passes_braced_through()
        => Assert.Equal("{ENTER}", KeyCombo.ToSendKeysSequence("{ENTER}"));

    [Fact]
    public void SendKeysSequence_converts_combo()
        => Assert.Equal("^{ENTER}", KeyCombo.ToSendKeysSequence("Ctrl+Enter"));

    [Fact]
    public void SendKeysSequence_win_stays_literal()
        => Assert.Equal("Win+D", KeyCombo.ToSendKeysSequence("Win+D")); // SendKeys 不支持 Win

    [Fact]
    public void SendKeysLiteral_escapes_metachars()
        => Assert.Equal("a{+}b{(}c{)}", KeyCombo.ToSendKeysLiteral("a+b(c)"));

    [Fact]
    public void SendKeysLiteral_newline_to_enter()
        => Assert.Equal("a{ENTER}b", KeyCombo.ToSendKeysLiteral("a\r\nb"));

    [Fact]
    public void SendKeysLiteral_braces_escaped()
        => Assert.Equal("{{}{}}", KeyCombo.ToSendKeysLiteral("{}"));

    [Theory]
    [InlineData("", true)]
    [InlineData("{ENTER}", true)]
    [InlineData("{TAB}{ENTER}", true)]      // 合法序列：成对
    [InlineData("abc", true)]               // 无花括号：视为平衡
    [InlineData("{ENTE", false)]            // 缺右括号：运行时必抛
    [InlineData("ENTER}", false)]           // 多右括号
    [InlineData("{a}}", false)]
    public void BracesBalanced_only_flags_unbalanced(string s, bool expected)
        => Assert.Equal(expected, KeyCombo.BracesBalanced(s));
}
