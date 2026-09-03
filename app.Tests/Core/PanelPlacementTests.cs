using Clockwork.Core;
using Xunit;

// 快捷面板的摆位：光标落在面板正中，整体夹进工作区。全部单位是物理像素。
// 这里测的每一条都是「手动拖窗口很难复现、但用户天天会踩」的边界：
// 光标贴屏幕角、副屏挂在主屏左边（工作区左边界是负数）、面板比屏幕还大。
public class PanelPlacementTests
{
    // 一块 1920×1080、任务栏在下方的主屏
    private const int L = 0, T = 0, R = 1920, B = 1040;
    private const int W = 400, H = 300;   // 面板尺寸

    [Fact]
    public void The_cursor_lands_in_the_middle_of_the_panel()
    {
        var (x, y) = PanelPlacement.Place(960, 520, W, H, L, T, R, B);
        Assert.Equal(960 - W / 2, x);
        Assert.Equal(520 - H / 2, y);
        // 换个说法再确认一遍：光标确实落在面板矩形的正中
        Assert.Equal(960, x + W / 2);
        Assert.Equal(520, y + H / 2);
    }

    // 贴着屏幕四角时，居中会把面板推出屏幕——夹回来，而不是让半个面板跑到看不见的地方。
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1919, 1039)]
    [InlineData(1919, 0)]
    [InlineData(0, 1039)]
    [InlineData(960, 520)]
    [InlineData(5, 1035)]
    public void Always_lands_fully_inside_the_work_area(int cx, int cy)
    {
        var (x, y) = PanelPlacement.Place(cx, cy, W, H, L, T, R, B);
        Assert.True(x >= L && y >= T && x + W <= R && y + H <= B,
            $"光标({cx},{cy}) → 面板({x},{y},{W}x{H}) 越出了工作区");
    }

    [Fact]
    public void Clamps_at_the_top_left_corner()
    {
        var (x, y) = PanelPlacement.Place(0, 0, W, H, L, T, R, B);
        Assert.Equal(L, x);
        Assert.Equal(T, y);
    }

    [Fact]
    public void Clamps_at_the_bottom_right_corner()
    {
        var (x, y) = PanelPlacement.Place(1919, 1039, W, H, L, T, R, B);
        Assert.Equal(R - W, x);
        Assert.Equal(B - H, y);
    }

    // 副屏在主屏左侧：工作区左/上边界为负，夹取必须对着实参算，不能假设从 0 起。
    [Fact]
    public void Honours_a_negative_work_area_origin()
    {
        var (x, y) = PanelPlacement.Place(-1900, -1000, W, H, -1920, -1080, 0, -40);
        Assert.True(x >= -1920, $"x={x} 跑到副屏左边界外了");
        Assert.True(y >= -1080, $"y={y} 跑到副屏上边界外了");
        Assert.True(x + W <= 0 && y + H <= -40);
    }

    // 副屏正中同样是居中，不受负坐标影响。
    [Fact]
    public void Centres_normally_on_a_secondary_screen_with_negative_coordinates()
    {
        var (x, y) = PanelPlacement.Place(-960, -560, W, H, -1920, -1080, 0, -40);
        Assert.Equal(-960 - W / 2, x);
        Assert.Equal(-560 - H / 2, y);
    }

    // 面板比工作区还大（超小分辨率 + 一屏放不下的格子数）：左上角必须留在屏内。
    // 这是顺序承重的那一条——先顶左上、再收右下，反过来会把左上角推到屏幕外，
    // 而标题和第一排格子正好在那一角。
    [Fact]
    public void Keeps_the_top_left_visible_when_the_panel_is_bigger_than_the_screen()
    {
        var (x, y) = PanelPlacement.Place(300, 300, 2000, 1200, L, T, R, B);
        Assert.Equal(L, x);
        Assert.Equal(T, y);
    }
}
