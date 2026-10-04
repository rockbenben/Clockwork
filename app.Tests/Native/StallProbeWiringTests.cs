using System.IO;
using Xunit;

// 卡顿探针的**接线**守卫：每一格具名计时必须正好包住它该包的那一句调用。
//
// 为什么要有这份文件：clockwork.error.log 里已经能写出 `trail-point took 8375ms (thread=ui)`
// 和 `hook-callback took 6469ms chain=0ms ours=6469ms`——时长有了，位置没有。用户报的
// 「轨迹画到一半卡住不动」就是这 8 秒：笔迹靠 DispatcherTimer 收（见 TrailStaleBitmapTests），
// UI 线程被按住多久，那条线就在屏幕上冻多久；超过 5 秒还会让 DWM 给整屏置顶窗盖一层幽灵窗，
// 那时症状从「卡一下」变成「桌面点不动」。
//
// 而"把某一句包进计时里"这件事，编译器与现有测试都不管：把 `End("trail-place")` 挪到
// `ShowGated()` 前后、或者整个删掉，代码照样编译、行为照样对，只是下一次复现又只剩一个时长。
// 这份守卫钉的就是那个位置。
//
// 为什么是源码文本而不是行为断言：这些路径要慢下来才有读数，而测试没法让操作系统故意慢
//（真去 Sleep 进生产代码就是另一种破坏）。所以钉结构，读数留给现场。
public class StallProbeWiringTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d.FullName;
    }

    private static string Src(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = Root();
        System.Array.Copy(parts, 0, all, 1, parts.Length);
        var p = Path.Combine(all);
        Assert.True(File.Exists(p), $"探针守卫找不到 {p}：文件搬过就要跟着改这里");
        return File.ReadAllText(p);
    }

    // 取一个方法的方法体：从签名到它那一层缩进的收尾大括号（同 TrailStaleBitmapTests 的口径）。
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature);
        Assert.True(start >= 0, $"源码里找不到 {signature}：这段守卫要跟着它走");
        int end = src.IndexOf("\n    }", start);
        Assert.True(end > start, $"{signature} 的方法体没找到收尾括号");
        return src.Substring(start, end - start);
    }

    /// <summary>探针必须**正好**包住那一句话：Begin 在它前面，End 在它后面。</summary>
    private static void AssertWraps(string body, string probe, string call)
    {
        int endAt = body.IndexOf($"End(\"{probe}", System.StringComparison.Ordinal);
        Assert.True(endAt >= 0,
            $"方法体里没有名为 {probe} 的计时收尾——这格探针被删了或改了名，下一次卡顿又会没有名字");
        int callAt = body.LastIndexOf(call, endAt, System.StringComparison.Ordinal);
        Assert.True(callAt >= 0, $"方法体里找不到被 {probe} 该包住的调用：{call}");
        int beginAt = body.LastIndexOf("UiStallWatch.Begin()", endAt, System.StringComparison.Ordinal);
        Assert.True(beginAt >= 0, $"{probe} 有 End 却没有 Begin");
        Assert.True(beginAt < callAt && callAt < endAt,
            $"{probe} 没有正好包住 {call}（Begin 在 {beginAt}、调用在 {callAt}、End 在 {endAt}）："
            + "计时挪到别的语句上，读数就会指错地方");
    }

    // ── 笔迹窗内部三段：8375ms 那条只能定位到 Point()，这三格把它分成摆位 / 加点 / 显窗 ──
    //
    // 分法是有依据的：同一条 `trail-point took 8375ms` 的卡顿行报的是 trailUp=never，也就是
    // **窗那一刻还没显示**——秒级只能落在 CoverMonitorOf（首个点会在这里造 HWND 并写位置）
    // 或紧随其后的 Show 上。分层窗每帧软件合成那条路则看 alloc=，此前没爆。
    [Fact]
    public void Trail_point_times_placement_add_and_show_separately()
    {
        var body = Body(Src("app", "Views", "GestureTrailWindow.cs"), "public void Point(");
        AssertWraps(body, "trail-place", "CoverMonitorOf(px, py)");
        AssertWraps(body, "trail-add", "_core.Points.Add(new Point(x, y))");
        AssertWraps(body, "trail-show", "ShowGated()");
    }

    // ── 钩子线程那边：UI 线程等满两次 Join 是已确认的一条路，得能说出是谁不让它走 ──
    [Fact]
    public void Dispose_times_the_ui_join_and_the_unhook()
    {
        var body = Body(Src("app", "Native", "MouseHook.cs"), "public void Dispose()");
        AssertWraps(body, "hook-join-first", "t.Join(UiJoinBudgetMs)");
        AssertWraps(body, "hook-unhook", "UnhookWindowsHookEx(h)");
        // 剩下的那一次等待必须在后台那一格里，不能回到 UI 线程上。
        AssertWraps(body, "hook-join-background", "t.Join(BackgroundJoinBudgetMs)");
    }

    // UiJoinBudgetMs 是「UI 线程愿意为一件本来亚毫秒的事冻多久」。等满 200ms 与等满 1000ms
    // 在功能上一字不差、测试全绿，唯一区别是屏幕上那条线多挂 800 毫秒——
    // 而那正是用户报的「轨迹卡住」。0 也是合法但错的值：等于完全不等，Unhook 会抢在
    // 泵线程处理 WM_QUIT 之前发生，还键的那三次注入就没人做了。
    [Fact]
    public void The_ui_thread_pays_only_a_short_join_when_the_hook_thread_is_stuck()
    {
        var src = Src("app", "Native", "MouseHook.cs");
        var m = System.Text.RegularExpressions.Regex.Match(src, @"UiJoinBudgetMs\s*=\s*(\d+)");
        Assert.True(m.Success, "UiJoinBudgetMs 不在了：UI 线程那次等待要有名字和一个上限");
        int budget = int.Parse(m.Groups[1].Value);
        Assert.InRange(budget, 50, 400);
        // 旧写法在 Dispose 里是 1000，而且还要再来一次（合计 2 秒）。第二次长等必须已经搬去后台。
        // 只查 Dispose 那一段：Install 那边也有 t.Join(1000)，但它跑在专门起出来的安装线程上、
        // 不碰 UI 线程（OffUiThreadTests 正是钉这一条的），拿它当违例是误报。
        var dispose = Body(Src("app", "Native", "MouseHook.cs"), "public void Dispose()");
        Assert.DoesNotContain("t.Join(1000)", dispose);
    }

    // 拆除时那三句「把欠系统的键还回去」各是一次 SendInput，而它排在钩子线程退出的路上：
    // Join 等满的因，多半就在这。
    [Fact]
    public void Teardown_times_the_injected_replays()
    {
        var body = Body(Src("app", "Native", "MouseHook.cs"), "private void Teardown()");
        AssertWraps(body, "hook-teardown-inject", "ReplayUp()");
    }

    // 回调里仅有的两处跨进程查询。日志的 ours= 排除了 CallNextHookEx 与注入，剩下的嫌疑就是
    // 这两句加一次 BeginInvoke——而源码里那句「微秒级 P/Invoke」此前只是断言。
    [Fact]
    public void Callback_times_the_two_cross_process_lookups()
    {
        var body = Body(Src("app", "Native", "MouseHook.cs"), "private IntPtr CallbackCore");
        AssertWraps(body, "hook-window-at", "Win32.WindowAtPoint(data.X, data.Y)");
        AssertWraps(body, "hook-proc-name", "Win32.ProcessNameForWindow(_gestureOrigin)");
    }

    // ── UI 线程侧的投递与收笔：这两格原本就在，钉在这里是为了让整张地图只有一个出处 ──
    [Fact]
    public void Post_and_trail_callbacks_keep_their_probes()
    {
        AssertWraps(Body(Src("app", "Services", "GestureCoordinator.cs"), "public void ApplyMouseHook()"),
                    "ui-post", "Disp.BeginInvoke(a)");
        AssertWraps(Body(Src("app", "Services", "GestureCoordinator.cs"), "private void DisposeMouseHook"),
                    "hook-dispose:", "_mouseHook.Dispose()");
        AssertWraps(Body(Src("app", "Services", "GestureCoordinator.cs"), "private void TrailPoint"),
                    "trail-point", "_trail?.Point(x, y, armed)");
        AssertWraps(Body(Src("app", "Services", "GestureCoordinator.cs"), "private void TrailEnd"),
                    "trail-finish", "_trail?.Finish()");
    }
}
