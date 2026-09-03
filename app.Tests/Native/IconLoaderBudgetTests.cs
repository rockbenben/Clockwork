using System.Diagnostics;
using Clockwork.Native;
using Xunit;

// 取图标这条路**全在 UI 线程上**（面板每个格子、面板管理器每一页、三个编辑器的预览，
// 六处同步调用），而图标路径是用户填的，可以指向一个已断开的网络盘——那时 File.Exists
// 会卡满 SMB 超时（几十秒）。
//
// 更要命的是那条线程同时是**低级鼠标钩子的泵**：被占住超过 LowLevelHooksTimeout
//（默认 300ms），Windows 会把钩子静默卸掉，而且不通知任何人——中键长按和全部右键手势
// 从此失灵，直到重启。所以这里量的不是「放弃得够快」，是「根本没去等」。
public class IconLoaderBudgetTests
{
    // 不可路由的地址：这台机器上没有它，只能靠超时放弃。
    private const string Dead = @"\\10.255.255.1\share\nope.png";
    private const string Dead2 = @"\\10.255.255.2\share\nope.png";

    [Fact]
    public void A_dead_network_path_does_not_block_at_all()
    {
        var sw = Stopwatch.StartNew();
        var bmp = IconLoader.Load(Dead);
        sw.Stop();
        Assert.Null(bmp);   // 这一轮没有图标，调用方回退到线描字形
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"取图标占了调用线程 {sw.ElapsedMilliseconds}ms，钩子可能已经被卸掉了");
    }

    // 一页格子常常指向同一个 exe。每格都拉起一次后台加载是白费，
    // 而同一条死路径被并发重试几十次更糟。
    [Fact]
    public void Many_tiles_pointing_at_one_dead_path_stay_cheap()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 24; i++) IconLoader.Load(Dead2);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"24 个格子占了 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void An_empty_path_is_not_a_lookup()
        => Assert.Null(IconLoader.Load("   "));
}
