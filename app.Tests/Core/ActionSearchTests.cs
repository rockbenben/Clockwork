using Clockwork.Core;
using Xunit;

// 搜索的匹配与排序。每一条都对着一次真实的打字。
public class ActionSearchTests
{
    private sealed record Item(string Name, string? Tip = null);

    private static string[] Run(string query, params Item[] items)
        => ActionSearch.Rank(items, query, x => x.Name, x => x.Tip).Select(x => x.Name).ToArray();

    private static readonly Item[] Panel =
    {
        new("截图"), new("息屏"), new("锁屏"), new("显示桌面"),
        new("静音"), new("闭麦"), new("免打扰"), new("恢复通知"),
        new("双屏扩展"), new("仅主屏"), new("清剪贴板"), new("任务管理器"),
        new("微信"), new("PicGo"), new("Quicker"),
    };

    // ── 拼音首字母 ──
    [Theory]
    [InlineData("jt", "截图")]
    [InlineData("sp", "锁屏")]
    [InlineData("qjtb", "清剪贴板")]
    [InlineData("rwglq", "任务管理器")]
    [InlineData("mdr", "免打扰")]
    public void Finds_by_pinyin_initials(string query, string expected)
        => Assert.Contains(expected, Run(query, Panel));

    // ── 空格分词，全部命中 ──
    [Fact]
    public void Every_token_must_match()
    {
        Assert.Equal(new[] { "清剪贴板" }, Run("清 板", Panel));
        Assert.Empty(Run("清 微信", Panel));   // 两个词分别都在，但不在同一项里
    }

    [Fact]
    public void Tokens_can_mix_hanzi_and_pinyin()
        => Assert.Equal(new[] { "清剪贴板" }, Run("清 tb", Panel));

    // ── 排序 ──
    [Fact]
    public void Name_prefix_beats_name_middle()
    {
        var r = Run("屏", new Item("锁屏"), new Item("屏幕亮度"));
        Assert.Equal(new[] { "屏幕亮度", "锁屏" }, r);
    }

    [Fact]
    public void Name_match_beats_pinyin_match()
    {
        // 「sp」：既是「锁屏」的首字母，也出现在「sputnik」的名字里。名字里那个更直接。
        var r = Run("sp", new Item("锁屏"), new Item("sputnik"));
        Assert.Equal(new[] { "sputnik", "锁屏" }, r);
    }

    [Fact]
    public void Name_beats_tip()
    {
        var r = Run("锁", new Item("离开", "锁屏并静音"), new Item("锁屏"));
        Assert.Equal(new[] { "锁屏", "离开" }, r);
    }

    // 稳定排序是搜索能用的前提：同分项每多打一个字就换一次位置的话，眼睛跟不上。
    [Fact]
    public void Ties_keep_their_original_order()
    {
        var r = Run("屏", new Item("息屏"), new Item("锁屏"), new Item("仅主屏"));
        Assert.Equal(new[] { "息屏", "锁屏", "仅主屏" }, r);
    }

    // ── 边界 ──
    [Fact]
    public void An_empty_query_returns_everything_unchanged()
    {
        Assert.Equal(Panel.Length, Run("", Panel).Length);
        Assert.Equal(Panel.Length, Run("   ", Panel).Length);
        Assert.Equal("截图", Run("", Panel)[0]);
    }

    [Fact]
    public void No_match_returns_nothing()
        => Assert.Empty(Run("zzzz", Panel));

    [Fact]
    public void Latin_names_match_case_insensitively()
        => Assert.Equal(new[] { "PicGo" }, Run("picgo", Panel));

    // 说明里的内容也搜得到——用户常常只记得「那个会弹提示的」而不记得它叫什么。
    [Fact]
    public void Searches_the_tip_too()
        => Assert.Equal(new[] { "收工·下班" }, Run("记录", new Item("收工·下班", "今天的任务 / 复习都记录好了吗？"), new Item("截图")));
}
