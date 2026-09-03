using System.Collections.Generic;
using Clockwork.Core;
using Xunit;

// 动作热键查重。这道检查存在的理由写在编辑器里：等运行时注册失败才报，用户可能早已关掉编辑器，
// 而那句失败提示还会把人指去查**别的程序**——占着它的却是本程序自己。
public class HotkeyConflictTests
{
    private static readonly (string Key, string Owner)[] Fn =
        { ("Ctrl+Alt+Q", "急停键"), ("Ctrl+Alt+Space", "快捷面板键") };

    private static ActionGroup G(string id, string name, string key, bool on = true)
        => new() { Id = id, Name = name, Hotkey = key, Enabled = on };

    // **回归**：此前只比对急停键，漏掉快捷面板键——而 Ctrl+Alt+Space 正是面板键的**默认值**，
    // 所以这一条一点都不刁钻：给动作配上它会被安静收下，然后那条动作永远按不响。
    [Fact]
    public void The_panel_hotkey_counts_as_taken()
        => Assert.Equal("快捷面板键", HotkeyConflict.OwnerOf("Ctrl+Alt+Space", null, "me", Fn));

    [Fact]
    public void The_stop_hotkey_counts_as_taken()
        => Assert.Equal("急停键", HotkeyConflict.OwnerOf("Ctrl+Alt+Q", null, "me", Fn));

    // 大小写与首尾空白不算另一个键——录进来的和手改 json 写进去的未必同一种写法。
    [Theory]
    [InlineData("ctrl+alt+space")]
    [InlineData("  Ctrl+Alt+Space  ")]
    public void Case_and_padding_do_not_free_a_key(string key)
        => Assert.Equal("快捷面板键", HotkeyConflict.OwnerOf(key, null, "me", Fn));

    [Fact]
    public void Another_enabled_action_owns_its_key()
    {
        var gs = new List<ActionGroup> { G("a", "专注", "Ctrl+Alt+J") };
        Assert.Equal("专注", HotkeyConflict.OwnerOf("Ctrl+Alt+J", gs, "me", Fn));
    }

    // 禁用的动作让出组合：用户禁用 A 正是为了把键腾给 B，在这儿反着拦就把那条路堵死了。
    [Fact]
    public void A_disabled_action_yields_its_key()
    {
        var gs = new List<ActionGroup> { G("a", "专注", "Ctrl+Alt+J", on: false) };
        Assert.Null(HotkeyConflict.OwnerOf("Ctrl+Alt+J", gs, "me", Fn));
    }

    // 自己不和自己撞：编辑一个已有热键的动作、什么都不改就按确定，不该被自己拦下。
    [Fact]
    public void An_action_does_not_conflict_with_itself()
    {
        var gs = new List<ActionGroup> { G("me", "专注", "Ctrl+Alt+J") };
        Assert.Null(HotkeyConflict.OwnerOf("Ctrl+Alt+J", gs, "me", Fn));
    }

    [Fact]
    public void A_free_key_has_no_owner()
        => Assert.Null(HotkeyConflict.OwnerOf("Ctrl+Alt+K", new List<ActionGroup> { G("a", "专注", "Ctrl+Alt+J") }, "me", Fn));

    // 没填热键不算冲突（绝大多数动作都不带热键）。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_hotkey_is_no_conflict(string? key)
        => Assert.Null(HotkeyConflict.OwnerOf(key, null, "me", Fn));

    // 空表 / null 不许炸：功能键清单在截图与冒烟里是空的。
    [Fact]
    public void Empty_inputs_are_not_a_crash()
        => Assert.Null(HotkeyConflict.OwnerOf("Ctrl+Alt+J", null, null, null));
}
