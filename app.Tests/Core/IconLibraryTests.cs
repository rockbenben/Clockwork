using Clockwork.Core;
using Xunit;

// 内置图标库。这里钉的是**清单本身的性质**——码位长什么样只有渲染看得见（见 IconLibrary 的类注释），
// 测试管不了；但「有没有重复」「铺不铺得满」这类事，眼睛反倒容易漏。
public class IconLibraryTests
{
    [Fact]
    public void No_duplicates()
    {
        var seen = new HashSet<int>();
        var dup = IconLibrary.Glyphs.Where(c => !seen.Add(c)).ToArray();
        Assert.Empty(dup);
    }

    // 选择器按 16 列铺格子。不是整数倍时最后一行会缺一截，那一格空白看着像库坏了。
    [Fact]
    public void Fills_whole_rows_of_sixteen()
    {
        Assert.NotEmpty(IconLibrary.Glyphs);
        Assert.Equal(0, IconLibrary.Glyphs.Length % 16);
    }

    // 私用区之外的码位不会是图标字体里的东西，多半是抄错了一位。
    [Fact]
    public void All_sit_in_the_private_use_area()
    {
        foreach (var c in IconLibrary.Glyphs)
            Assert.InRange(c, 0xE000, 0xF8FF);
    }

    [Fact]
    public void Format_round_trips_through_Parse()
    {
        foreach (var c in IconLibrary.Glyphs)
            Assert.Equal(c, IconLibrary.Parse(IconLibrary.Format(c)));
    }

    [Fact]
    public void Format_pads_to_four_digits()
    {
        Assert.Equal("E72E", IconLibrary.Format(0xE72E));
    }

    [Theory]
    [InlineData("E72E", 0xE72E)]
    [InlineData("e72e", 0xE72E)]   // 手打的小写也该认
    [InlineData("0xE72E", 0xE72E)]
    [InlineData("  E72E  ", 0xE72E)]
    public void Parses_the_shapes_people_actually_type(string input, int expected)
        => Assert.Equal(expected, IconLibrary.Parse(input));

    // 图标字段同时收路径，所以「不是码位」必须答得干脆，否则路径会被当成码位吃掉。
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"D:\pics\icon.png")]
    [InlineData("icon.png")]
    [InlineData("E7")]        // 太短
    [InlineData("E72E12")]    // 太长
    [InlineData("ZZZZ")]      // 不是十六进制
    public void Anything_that_is_not_a_codepoint_reports_minus_one(string? input)
        => Assert.Equal(-1, IconLibrary.Parse(input));
}

// 格子阵宽度。面板宽度由它说了算，所以它必须只跟「列数 × 一格宽」有关——
// 一旦有别的东西混进来，面板又会悄悄比它装的东西宽。
public class PanelGridWidthTests
{
    [Fact]
    public void Is_columns_times_tile_width_plus_gaps()
    {
        var t = PanelMetrics.For("normal", false);
        Assert.Equal(4 * (t.Width + PanelMetrics.TileGap * 2), PanelMetrics.GridWidth("normal", false, 4));
    }

    [Fact]
    public void Grows_with_columns()
    {
        Assert.True(PanelMetrics.GridWidth("normal", false, 6) > PanelMetrics.GridWidth("normal", false, 4));
    }

    // 列数超范围时用夹过的值，不是原样乘进去——否则手改配置写个 40 列，
    // 面板会算出一个几千像素宽的窗口。
    [Fact]
    public void Clamps_absurd_column_counts()
    {
        Assert.Equal(PanelMetrics.GridWidth("normal", false, PanelMetrics.MaxColumns),
                     PanelMetrics.GridWidth("normal", false, 40));
    }

    [Fact]
    public void Icon_only_is_narrower()
    {
        Assert.True(PanelMetrics.GridWidth("normal", true, 4) < PanelMetrics.GridWidth("normal", false, 4));
    }
}
