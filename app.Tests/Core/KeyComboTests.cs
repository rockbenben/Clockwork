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
    [InlineData(null, true)]
    [InlineData("hello", true)]
    [InlineData("{ENTER}", true)]
    [InlineData("{enter}", true)]        // SendKeys 关键字大小写不敏感
    [InlineData("{ESCAPE}", true)]       // ESC/ESCAPE 双写法都在 WinForms 关键字表里，漏了会拒存能跑的步骤
    [InlineData("{CLEAR}", true)]
    [InlineData("^a%{F4}", true)]        // 修饰前缀 + 功能键
    [InlineData("{LEFT 4}", true)]       // 重复次数：恰好一个空白 + 纯数字 + 紧跟 }
    [InlineData("{} 5}", true)]          // 「{} n}」特例：发 n 个字面 }（真实解析器专门分支支持）
    [InlineData("{{}", true)]            // 字面 {
    [InlineData("{}}", true)]            // 字面 }
    [InlineData("{{}{}}", true)]         // ToSendKeysLiteral("{}") 的输出必须被判合法（校验与转义闭环）
    [InlineData("{%}", true)]            // 单字符元字符组
    [InlineData("+(abc)", true)]         // 圆括号分组
    [InlineData("(a)(b)", true)]         // 顺序多组合法
    [InlineData("+(a(b))", true)]        // 嵌套 ≤3 层合法（真实解析器 cGrp>3 才抛）
    [InlineData("^(a(b(c)))", true)]     // 恰好 3 层
    [InlineData("{中}", true)]           // 单字符（非 ASCII）组
    [InlineData("{ENTE", false)]         // 花括号未闭合
    [InlineData("{ENTE}", false)]        // 未知键名
    [InlineData("{}", false)]            // 空组
    [InlineData("abc}", false)]          // 组外孤立 }
    [InlineData("(abc", false)]          // 圆括号未闭合
    [InlineData("abc)", false)]          // 无起括号的 )
    [InlineData("(a(b(c(d))))", false)]  // 第 4 层嵌套：真实解析器抛 SendKeysNestingError
    [InlineData("{LEFT x}", false)]      // 次数不是数字
    [InlineData("{LEFT  4}", false)]     // 双空格：真实解析器只跳一个空白、随后必须是数字（InvalidSendKeysRepeat）
    [InlineData("{LEFT 4 }", false)]     // 数字后必须紧跟 }（尾随空白必抛）
    [InlineData("{LEFT -4}", false)]     // 符号不是数字
    [InlineData("{F17}", false)]         // SendKeys 只到 F16
    public void IsValidSendKeys_accepts_legal_rejects_guaranteed_throws(string? s, bool ok)
        => Assert.Equal(ok, KeyCombo.IsValidSendKeys(s));

    // —— 手输组合键的修饰键拼写校验 ——
    // ParseCombo 对不认识的段是「当成主键」、后面的段再覆盖它，于是 "Ctrl+Shft+A" 静默变成 Ctrl+A。
    // 框里显示一套、发出去另一套，是本校验要挡的东西（捕捉出来的串不会畸形，只有手输会）。
    [Theory]
    [InlineData("Ctrl+Shift+A", false)]   // 正常
    [InlineData("Win+6", false)]          // 手输的主要用途：系统占用的 Win 组合捕捉不到
    [InlineData("Win+Ctrl+Alt+Shift+F1", false)]
    [InlineData("F5", false)]             // 裸键（发送模式允许）
    [InlineData("control+a", false)]      // 别名 + 小写照样认
    [InlineData("Ctrl+Shft+A", true)]     // Shift 拼错 → 实际会发 Ctrl+A
    [InlineData("Wn+D", true)]            // Win 拼错 → 实际会发 D
    [InlineData("Ctrl+Shift+A+B", true)]  // 多打一段 → A 被 B 覆盖
    public void HasUnknownModifier_flags_typos_in_modifier_positions(string combo, bool bad)
        => Assert.Equal(bad, KeyCombo.HasUnknownModifier(combo));

    // 拼错的串真的会变成另一个键——上面那条校验存在的理由，不是理论担忧。
    [Fact]
    public void Typo_in_modifier_silently_becomes_a_different_combo()
    {
        var p = KeyCombo.ParseCombo("Ctrl+Shft+A");
        Assert.Equal("A", p.Key);
        Assert.DoesNotContain("Shift", p.Modifiers);   // 用户以为按的是 Ctrl+Shift+A
    }
}
