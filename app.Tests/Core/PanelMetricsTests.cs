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

    [Fact]
    public void Page_capacity_is_rows_times_columns()
    {
        Assert.Equal(28, PanelMetrics.PageCapacity(3, 4, 4));    // 默认版面
        Assert.Equal(12, PanelMetrics.PageCapacity(3, 0, 4));    // 单条带
        Assert.Equal(48, PanelMetrics.PageCapacity(4, 4, 6));
    }

    // 容量 0 会让分页切出无穷多张空页。设置页给不出 0+0，但配置文件是能手改的。
    [Fact]
    public void A_zero_row_config_still_yields_a_usable_capacity()
    {
        Assert.True(PanelMetrics.PageCapacity(0, 0, 4) > 0);
        Assert.True(PanelMetrics.PageCapacity(-5, -5, 4) > 0);
    }

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
