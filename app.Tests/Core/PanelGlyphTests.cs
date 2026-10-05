using Clockwork.Core;
using Xunit;

// 面板格子的图标。这里只能钉「每种步骤都有图标、且各不相同」这类结构性事实——
// 「这个码位画出来是不是键盘」字体表说了不算，得靠 --shots 看截图（见 PanelGlyph 的注释）。
public class PanelGlyphTests
{
    // 每一种步骤类型都必须有图标：漏一种，那一格在一片有图标的格子里只剩文字，看着像坏了。
    // 用 StepDisplay.StepKinds 而不是手抄一份清单——新增步骤类型时这条会自动跟着覆盖到。
    [Fact]
    public void Every_step_kind_has_a_glyph()
    {
        Assert.All(StepDisplay.StepKinds, k =>
            Assert.False(string.IsNullOrWhiteSpace(PanelGlyph.ForKind(k)), $"步骤类型 {k} 没有图标"));
    }

    // 未知/空类型不返回空串，走兜底。配置是可以手改的，冒出一个没人认识的 kind 不该让面板出现空格子。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("someKindFromTheFuture")]
    public void An_unknown_kind_falls_back_instead_of_going_blank(string? kind)
        => Assert.Equal(PanelGlyph.Fallback, PanelGlyph.ForKind(kind));

    // 图标是拿来区分类型的，几种撞在一起就失去意义了。
    // "group" 有意与整组图标相同（引用一个组和整组一格说的是同一件事），故排除后再查重复。
    [Fact]
    public void Distinct_kinds_get_distinct_glyphs()
    {
        var kinds = System.Array.FindAll(StepDisplay.StepKinds, k => k != "group");
        var glyphs = System.Array.ConvertAll(kinds, PanelGlyph.ForKind);
        Assert.Equal(glyphs.Length, new System.Collections.Generic.HashSet<string>(glyphs).Count);
    }

    [Fact]
    public void A_group_step_shares_the_group_glyph()
        => Assert.Equal(PanelGlyph.Group, PanelGlyph.ForKind("group"));

    // 脚本字形撞进步骤类型那一批就白加了：一格花括号和一格「发送文本」的铅笔得能分开。
    [Fact]
    public void Script_glyphs_dont_collide_with_kind_glyphs()
    {
        var kinds = StepDisplay.StepKinds.Select(PanelGlyph.ForKind).ToHashSet();
        Assert.NotEmpty(kinds);
        Assert.DoesNotContain(PanelGlyph.Console, kinds);
        Assert.DoesNotContain(PanelGlyph.Code, kinds);
    }

    // 认不出来的目标要答 null，让 PanelIcon 照旧去借目标自己的图标——
    // 把 .exe 也当成脚本的话，整个面板的彩色图标会一夜之间变成线描。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"C:\Start Menu\微信.lnk")]
    [InlineData(@"D:\art\icon.png")]
    [InlineData("https://example.com")]
    [InlineData(@"D:\notes\run.me")]        // 后缀像 .me，不是 .ps1
    public void Anything_that_is_not_a_script_reports_no_glyph(string? path)
        => Assert.Null(PanelGlyph.ForScript(path));

    // 都该是单个字符（MDL2 是基本多文种平面内的私用区码位）。写成两个字符通常意味着
    // 码位抄错成了需要代理对的位置，那种字符 Segoe MDL2 Assets 里根本没有，只会画出方框。
    [Fact]
    public void Glyphs_are_single_characters()
    {
        Assert.All(StepDisplay.StepKinds, k => Assert.Equal(1, PanelGlyph.ForKind(k).Length));
        Assert.Equal(1, PanelGlyph.Group.Length);
        Assert.Equal(1, PanelGlyph.Fallback.Length);
        Assert.Equal(1, PanelGlyph.Console.Length);
        Assert.Equal(1, PanelGlyph.Code.Length);
    }
}
