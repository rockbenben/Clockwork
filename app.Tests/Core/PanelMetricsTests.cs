using System.Collections.Generic;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 面板格子的尺寸表与设置项的取值把关。
public class PanelMetricsTests
{
    [Fact]
    public void Columns_clamp_to_the_supported_range()
    {
        Assert.Equal(PanelMetrics.MinColumns, PanelMetrics.ClampColumns(0));
        Assert.Equal(PanelMetrics.MinColumns, PanelMetrics.ClampColumns(-5));
        Assert.Equal(PanelMetrics.MaxColumns, PanelMetrics.ClampColumns(99));
        Assert.Equal(5, PanelMetrics.ClampColumns(5));
    }

    // 手改配置写进来一个不认识的档位不该让面板画不出来——归到标准档。
    [Theory]
    [InlineData(null, "normal")]
    [InlineData("", "normal")]
    [InlineData("HUGE", "normal")]
    [InlineData("compact", "compact")]
    [InlineData("roomy", "roomy")]
    public void An_unknown_size_normalizes_to_normal(string? input, string expected)
        => Assert.Equal(expected, PanelMetrics.NormalizeSize(input));

    [Fact]
    public void Every_listed_size_survives_normalization()
        => Assert.All(PanelMetrics.Sizes, s => Assert.Equal(s, PanelMetrics.NormalizeSize(s)));

    // 三档必须是真的一档比一档大——写错一个数就会出现「宽松比标准还小」这种没人会去核对的错。
    [Fact]
    public void The_three_sizes_actually_grow()
    {
        var c = PanelMetrics.For("compact");
        var n = PanelMetrics.For("normal");
        var r = PanelMetrics.For("roomy");
        Assert.True(c.Width < n.Width && n.Width < r.Width);
        Assert.True(c.Height < n.Height && n.Height < r.Height);
        Assert.True(c.GlyphFont < n.GlyphFont && n.GlyphFont < r.GlyphFont);
        Assert.True(c.LabelFont < n.LabelFont && n.LabelFont < r.LabelFont);
    }

    // 只显示图标：不留文字的位置（LabelFont=0 是「不画文字」的约定），格子同时收窄收矮。
    // 高度不跟着收的话，图标会居中浮在一大片空白里，看着像文字没加载出来。
    [Fact]
    public void Icon_only_drops_the_label_and_shrinks_the_tile()
    {
        Assert.All(PanelMetrics.Sizes, s =>
        {
            var full = PanelMetrics.For(s);
            var icon = PanelMetrics.For(s, iconOnly: true);
            Assert.Equal(0, icon.LabelFont);
            Assert.True(icon.Width < full.Width, $"{s}: 只显示图标时宽度没收");
            Assert.True(icon.Height < full.Height, $"{s}: 只显示图标时高度没收");
            Assert.True(icon.GlyphFont > 0);
        });
    }

    [Fact]
    public void Rows_clamp_to_the_supported_range()
    {
        Assert.Equal(PanelMetrics.MinRows, PanelMetrics.ClampRows(-3));
        Assert.Equal(PanelMetrics.MaxRows, PanelMetrics.ClampRows(99));
        Assert.Equal(0, PanelMetrics.ClampRows(0));   // 0 合法：下带设 0 即退回单条带
        Assert.Equal(3, PanelMetrics.ClampRows(3));
    }

    // 一屏能看见几格 = （上带行数 + 下带行数）× 列数。
    // **这不是一页的上限**：一页装多少由用户自己决定，装不下的在面板里往下滚。
    [Fact]
    public void Visible_capacity_is_rows_times_columns()
    {
        Assert.Equal(28, PanelMetrics.VisibleCapacity(3, 4, 4));    // 默认版面
        Assert.Equal(12, PanelMetrics.VisibleCapacity(3, 0, 4));    // 单条带
        Assert.Equal(48, PanelMetrics.VisibleCapacity(4, 4, 6));
    }

    // 两个行数都是 0 时给个兜底值而不是 0：0 会让「一屏放得下吗」这个问题没有答案。
    // 设置页给不出 0+0，但配置文件是能手改的。
    [Fact]
    public void A_zero_row_config_still_yields_a_usable_capacity()
    {
        Assert.True(PanelMetrics.VisibleCapacity(0, 0, 4) > 0);
        Assert.True(PanelMetrics.VisibleCapacity(-5, -5, 4) > 0);
    }

    // 一行有多高：格子高 + 上下各一个空隙。
    [Fact]
    public void A_row_is_a_tile_plus_the_gaps()
    {
        Assert.Equal(78, PanelMetrics.RowHeight("normal", false));    // 76 + 2
        Assert.Equal(68, PanelMetrics.RowHeight("compact", false));   // 66 + 2
        Assert.Equal(60, PanelMetrics.RowHeight("normal", true));     // 58 + 2
    }

    // 「每屏几行」与「分带在哪」换算成面板内部的那对数（上带行数、下带行数）。
    //
    // 设置面只给这两个数，下带行数是**算出来的**——而它们俩加起来必须正好是每屏行数，
    // 否则面板露出来的行数就与设置说的不是一个数（那正是「说空话」）。
    [Fact]
    public void Bands_are_split_from_the_rows_per_screen()
    {
        Assert.Equal((3, 4), PanelMetrics.BandsOf(7, 3));   // 默认：3 + 4
        Assert.Equal((3, 3), PanelMetrics.BandsOf(6, 3));
        Assert.Equal((1, 5), PanelMetrics.BandsOf(6, 1));
        Assert.Equal((7, 0), PanelMetrics.BandsOf(7, 7));   // 上带 = 每屏 = 单条带
    }

    // 越界一律夹住：上带不能超过每屏（下带会成负数），每屏至少一行。
    [Fact]
    public void Bands_clamp_out_of_range_input()
    {
        Assert.Equal((5, 0), PanelMetrics.BandsOf(5, 9));   // 上带超过每屏 → 夹到每屏
        Assert.Equal((1, 0), PanelMetrics.BandsOf(0, 0));   // 0 行 → 至少一行
    }

    // 这块屏放得下几行：工作区高度减去窗口自己的高度与两坑的边，再除以一行。
    //
    // 行数是**上限**，不是承诺——屏幕不够时面板会少显示几行（往下滚看得到）。
    // 管理器把这句话如实说给用户（见 PanelManagerWindow.UpdateScreenRowsNote），
    // 所以这个算式与面板那侧必须是同一份（PanelMetrics.WindowChrome / BandChrome）。
    [Fact]
    public void Screen_rows_follow_the_work_area()
    {
        // 最紧那一档（1366×768 @150%，可用高度 464）：(464-110-23)/78 = 4 行
        Assert.Equal(4, PanelMetrics.ScreenRows(464, "normal", false));
        // 1080p @150%（可用高度约 693）：(693-110-23)/78 = 7 行
        Assert.Equal(7, PanelMetrics.ScreenRows(693, "normal", false));
        // 紧凑档的行更矮（68 而非 78），同一块屏能多放一行
        Assert.Equal(8, PanelMetrics.ScreenRows(693, "compact", false));
    }

    // 量不到屏幕时返回 0——那是「量不到」，不是「一行也放不下」。
    [Fact]
    public void Screen_rows_are_zero_when_the_screen_cannot_be_measured()
        => Assert.Equal(0, PanelMetrics.ScreenRows(0, "normal", false));

    // 所有档位都必须给出可用的正数尺寸——0 或负数会让 WPF 把格子画成看不见的一点。
    [Fact]
    public void Every_combination_yields_a_usable_tile()
    {
        foreach (var s in PanelMetrics.Sizes)
            foreach (var iconOnly in new[] { false, true })
            {
                var m = PanelMetrics.For(s, iconOnly);
                Assert.True(m.Width > 0 && m.Height > 0 && m.GlyphFont > 0, $"{s}/{iconOnly} 尺寸不可用");
            }
    }
}

// 「只显示图标」档的 LabelFont 是 0，而 **WPF 的 FontSize 不接受 0**——赋值当场抛
// 「"0" 不是属性 "FontSize" 的有效值」。这类崩溃只在特定档位触发，靠人记得去点那个勾不可靠。
// 这里钉住两件事：哪些档会给 0（调用方必须守卫），以及哪些值任何档都不能是 0。
public class PanelMetricsZeroFontTests
{
    public static IEnumerable<object[]> AllCombos =>
        from s in PanelMetrics.Sizes from icon in new[] { false, true } select new object[] { s, icon };

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void Only_icon_only_mode_yields_a_zero_label_font(string size, bool iconOnly)
    {
        var m = PanelMetrics.For(size, iconOnly);
        if (iconOnly) Assert.Equal(0, m.LabelFont);
        else Assert.True(m.LabelFont > 0, $"{size}: 非「只显示图标」档不该给 0 字号");
    }

    // 这几个值在任何档位都会被直接赋给 WPF 属性，为 0 即崩。
    [Theory]
    [MemberData(nameof(AllCombos))]
    public void Sizes_that_are_always_assigned_are_never_zero(string size, bool iconOnly)
    {
        var m = PanelMetrics.For(size, iconOnly);
        Assert.True(m.GlyphFont > 0, "GlyphFont 直接进 FontSize");
        Assert.True(m.Width > 0 && m.Height > 0, "宽高直接进 Width/Height");
    }
}
