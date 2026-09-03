using System.Diagnostics;
using Clockwork.Core;
using Xunit;

// 取图标要决定「用哪个路径」，而那一步全在 UI 线程上（面板每次呼出、管理器每次重画、
// 三个编辑器的预览）。它原本做的是同步 File.Exists——一条断开的网络盘路径就能卡满 SMB 超时，
// 而 UI 线程同时是低级鼠标钩子的泵：占住超过 300ms，Windows 就把钩子静默卸掉，
// 中键长按和全部右键手势从此失灵。所以这条路一秒都不能等。
public class PathProbeTests
{
    private const string Dead = @"\\10.255.255.9\share\nope.exe";

    [Fact]
    public void A_dead_path_answers_immediately()
    {
        var sw = Stopwatch.StartNew();
        PathProbe.ExistsOptimistic(Dead);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 100, $"探测占了调用线程 {sw.ElapsedMilliseconds}ms");
    }

    // 一页里几十个格子指向同一个路径是常态，不能每格都拉起一次后台探测。
    [Fact]
    public void Many_asks_about_one_path_stay_cheap()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++) PathProbe.ExistsOptimistic(Dead);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"50 次探测占了 {sw.ElapsedMilliseconds}ms");
    }

    // 「先当它在」是乐观答案，只影响取图标先试哪一个；取不到会回退线描字形，两条路都不阻塞。
    [Fact]
    public void An_unknown_path_is_assumed_present_for_now()
        => Assert.True(PathProbe.ExistsOptimistic(@"\\10.255.255.8\share\unknown.exe"));

    [Fact]
    public void An_empty_path_is_never_present()
    {
        Assert.False(PathProbe.ExistsOptimistic(""));
        Assert.False(PathProbe.ExistsOptimistic("   "));
        Assert.False(PathProbe.ExistsOptimistic(null));
    }

    // 后台那次探测回来之后，答案会被回填成真的。
    //
    // **必须验 false 那一档。** 这条一度是「问一个真实存在的路径，睡一会儿，断言 true」——
    // 而缓存没命中时本来就返回 true（乐观答案），所以把整个回填删掉它照样绿：
    // 它测不出任何东西。只有「本来不存在」的路径能把两者区分开：第一次乐观地说在，
    // 回填之后如实说不在。
    [Fact]
    public void A_missing_path_settles_to_false()
    {
        // 用一个没被别的用例问过的本地路径：问过一次缓存里就是真答案了，第一次的乐观就看不到。
        const string gone = @"C:\__clockwork_settles_to_false__\nope.exe";
        Assert.True(PathProbe.ExistsOptimistic(gone));    // 冷缓存：先当它在
        System.Threading.Thread.Sleep(300);
        Assert.False(PathProbe.ExistsOptimistic(gone));   // 回填之后：如实说不在
    }

    // 启动程序那条路仍要**真答案**：它跑在工作线程上，等得起，而且猜错就是启动错东西。
    // 这里用一个**本地**的不存在路径，不用网络路径——网络那条要走满 SMB 超时（实测 21 秒），
    // 而这条断言要的是「它确实去问了盘」，不是去量超时有多久。
    [Fact]
    public void Launching_still_asks_the_disk_for_real()
    {
        var me = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var gone = @"C:\__clockwork_no_such_file__\nope.exe";
        Assert.Equal(me, LaunchTarget.ResolveLaunchTarget(gone, me, blocking: true));
    }

    // 不阻塞那一档「先当主路径在」，于是停在主路径上——这正是它与上面那条的分工。
    // 路径要用一个**没被别的用例问过**的：问过一次之后缓存里就是真答案了，
    // 那时它会正确地落到备用路径上，而这条用例想验的是冷缓存下的乐观行为。
    [Fact]
    public void The_icon_path_stays_optimistic_when_nothing_is_known_yet()
    {
        var me = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var unknown = @"\\10.255.255.7\share\never-asked.exe";
        Assert.Equal(unknown, LaunchTarget.ResolveLaunchTarget(unknown, me, blocking: false));
    }
}
