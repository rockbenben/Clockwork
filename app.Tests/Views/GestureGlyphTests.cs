using System.Linq;
using Clockwork.Views;
using Xunit;

// 手势管理器那张列表的立身之本是「认一条手势靠形状，不是读它的名字」。
// 那句话只有在**两条不同的手势画出来真的不一样**时才成立。
//
// 原路折返的手势会破坏它：↑↓ 在单位格上是 (0,0)→(0,-1)→(0,0)，两条腿画在同一条线上，
// 看起来就是一根竖线，与单独一条 ↓ 分不出来。默认手势里「搜索选中的文字」正是 ↑↓
// （人画尖角时手上就是这个形状），而「粘贴」正是 ↓ —— 两行缩略图长一个样。
public class GestureGlyphTests
{
    // 折返那一笔要走在去程旁边：横向必须真的张开，否则就是一根线。
    [Fact]
    public void A_retraced_stroke_is_not_drawn_on_top_of_itself()
    {
        var pts = GestureGlyph.Trace("UD");
        Assert.Equal(3, pts.Count);
        double spanX = pts.Max(p => p.X) - pts.Min(p => p.X);
        Assert.True(spanX > 0.2, $"↑↓ 被画成了一根竖线（横向跨度 {spanX}）");
        Assert.True(pts.Max(p => p.Y) - pts.Min(p => p.Y) > 0.9, "纵向该有一整格");
    }

    // 反面：不折返的手势不该被这条规则挪动，形状必须还是原来的直角。
    [Theory]
    [InlineData("R")]
    [InlineData("D")]
    [InlineData("RD")]
    [InlineData("93")]
    [InlineData("7")]
    public void Non_retraced_strokes_stay_on_the_unit_grid(string path)
    {
        foreach (var p in GestureGlyph.Trace(path))
        {
            Assert.Equal(p.X, System.Math.Round(p.X), 6);
            Assert.Equal(p.Y, System.Math.Round(p.Y), 6);
        }
    }

    // 「搜索」(↑↓) 与「粘贴」(↓) 必须画得出区别——这就是这个文件存在的全部理由。
    [Fact]
    public void The_search_gesture_does_not_look_like_paste()
    {
        var search = GestureGlyph.Trace("UD");
        var paste = GestureGlyph.Trace("D");
        Assert.NotEqual(search.Count, paste.Count);
    }

    // 非法字符整串作废（Make 据此显示水印而不是画一个空盒子）。
    [Theory]
    [InlineData("RX")]
    [InlineData("x")]
    public void Illegal_paths_trace_nothing(string path)
        => Assert.Empty(GestureGlyph.Trace(path));
}
