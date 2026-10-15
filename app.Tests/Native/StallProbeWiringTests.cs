using System.IO;
using System.Linq;
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

    // ── 钩子线程那边：调用线程等满 Join 是已确认的一条路，得能说出是谁不让它走 ──
    [Fact]
    public void Dispose_times_the_ui_join_and_the_unhook()
    {
        // 两条路（自愈 Dispose / 退出 DisposeForExit）共用这一个身体，探针跟着身体走。
        var src = Src("app", "Native", "MouseHook.cs");
        var body = Body(src, "private void DisposeCore(bool joinOnCallingThread)");
        AssertWraps(body, "hook-join-first", "t.Join(UiJoinBudgetMs)");
        AssertWraps(Body(src, "private void UnhookNow(IntPtr h)"), "hook-unhook", "UnhookWindowsHookEx(h)");
        // 剩下的那一次等待必须在后台那一格里，不能回到调用线程上。
        AssertWraps(Body(src, "private void HandOff(Thread t)"), "hook-join-background",
                    "t.Join(BackgroundJoinBudgetMs)");
    }

    // **顺序就是这次改动的全部内容**，所以钉顺序而不是钉名字：摘钩要排在通知泵线程与任何等待之前
    //（它一返回，链上就没有我们那套「吞掉右键按下」的逻辑了），而「等不等那条线程」才是两条路的分岔。
    //
    // 上一轮写反了：照着「UI 线程停在 unhook 里出不来」这个**整份日志里一条支持证据都没有**的判断
    //（`hook-unhook` 与 `hook-teardown-inject` 从没进过 slow 行，慢的一律是 `hook-join-background 30000ms`），
    // 把摘钩交给工作线程、先让调用线程等它退。部署后同一形状的壳进程又出现了一次。
    [Fact]
    public void The_chain_is_left_before_anything_that_can_wait()
    {
        var src = Src("app", "Native", "MouseHook.cs");
        var body = Body(src, "private void DisposeCore(bool joinOnCallingThread)");
        int unhook = body.IndexOf("UnhookNow(h);", System.StringComparison.Ordinal);
        int quit = body.IndexOf("PostThreadMessage(_threadId, WM_QUIT", System.StringComparison.Ordinal);
        int join = body.IndexOf("t.Join(UiJoinBudgetMs)", System.StringComparison.Ordinal);
        Assert.True(unhook >= 0 && quit >= 0 && join >= 0, "这三句不见了：这段守卫要跟着它们走");
        Assert.True(unhook < quit, "摘钩必须排在通知泵线程之前：先让链上没有我们，再谈别的");
        Assert.True(quit < join, "调用线程上那唯一一次等待要有名字、有上限，而且排在摘钩之后");
        // 退出这一路：调用线程走到 HandOff 就返回，一次 Join 都不付。
        Assert.Contains("if (!joinOnCallingThread) { HandOff(t); return; }", body);
        Assert.Contains("public void Dispose() => DisposeCore(joinOnCallingThread: true);", src);
        Assert.Contains("public void DisposeForExit() => DisposeCore(joinOnCallingThread: false);", src);
        // 退出收尾（App.ExitApp → GestureCoordinator.DisposeOnExit）必须点的是 DisposeForExit。
        Assert.Contains("_mouseHook?.DisposeForExit();",
                        Body(Src("app", "Services", "GestureCoordinator.cs"), "public void DisposeOnExit()"));
    }

    // UiJoinBudgetMs 是「UI 线程愿意为一件本来亚毫秒的事冻多久」。等满 200ms 与等满 1000ms
    // 在功能上一字不差、测试全绿，唯一区别是屏幕上那条线多挂 800 毫秒——
    // 而那正是用户报的「轨迹卡住」。0 也是合法但错的值：等于完全不等，还键与重装都会撞上
    // 一条还在跑的泵线程。
    [Fact]
    public void The_ui_thread_pays_only_a_short_join_when_the_hook_thread_is_stuck()
    {
        var src = Src("app", "Native", "MouseHook.cs");
        var m = System.Text.RegularExpressions.Regex.Match(src, @"UiJoinBudgetMs\s*=\s*(\d+)");
        Assert.True(m.Success, "UiJoinBudgetMs 不在了：UI 线程那次等待要有名字和一个上限");
        int budget = int.Parse(m.Groups[1].Value);
        Assert.InRange(budget, 50, 400);
        // 旧写法在 Dispose 里是 1000，而且还要再来一次（合计 2 秒）。第二次长等必须已经搬去后台。
        // 只查调用线程那一段身体：Install 那边也有 t.Join(1000)，但它跑在专门起出来的安装线程上、
        // 不碰 UI 线程（OffUiThreadTests 正是钉这一条的），拿它当违例是误报。
        Assert.DoesNotContain("t.Join(1000)", Body(src, "private void DisposeCore(bool joinOnCallingThread)"));
    }

    // 拆除时那几句「把欠系统的键还回去」各是一次 SendInput，而它们排在钩子线程退出的路上：
    // Join 等满的因，多半就在这。
    //
    // 名字跟着代码搬了家（现在在 SettleOwedKeys 里）：那三句现在有两个调用者——泵线程自己的
    // Teardown，以及泵线程**没退回来**时 DisposeCore 起的那条工作线程。探针留在方法内部，
    // 两边就都能量到，而这正是这次改动最想看见的那一格（哪一次还键把哪条线程按住了）。
    [Fact]
    public void Teardown_times_the_injected_replays()
    {
        var src = Src("app", "Native", "MouseHook.cs");
        AssertWraps(Body(src, "private void SettleOwedKeys()"), "hook-teardown-inject", "ReplayUp()");
        Assert.Contains("SettleOwedKeys();", Body(src, "private void Teardown()"));
    }

    // 泵线程回不来时，「还键」这一笔必须由那条等着它的工作线程做（否则用户那一下右键永久消失，
    // 而还键的代码躺在一个不会跑到的 finally 里）。改回「只在 Teardown 里还」编译照过、
    // 单测照绿，症状要等下一次链被堵住才出现，所以钉形状。
    [Fact]
    public void A_stranded_teardown_still_settles_the_owed_keys_off_the_calling_thread()
    {
        var body = Body(Src("app", "Native", "MouseHook.cs"), "private void HandOff(Thread t)");
        int run = body.IndexOf("Task.Run", System.StringComparison.Ordinal);
        int settle = body.IndexOf("SettleOwedKeys();", System.StringComparison.Ordinal);
        Assert.True(run >= 0 && settle > run,
            "还键必须排在 Task.Run 的那一段里：调用线程（自愈时是 UI 线程）一句 SendInput 都不许等");
        // 计数一进一出都要有，漏掉出 = 自愈从此再也不重装（比白重装一次糟得多）。
        Assert.Contains("Interlocked.Increment(ref _strandedThreads);", body);
        Assert.Contains("Interlocked.Decrement(ref _strandedThreads);", body);
    }

    // 自愈那一路必须读这一格，并且在它非零时**不动手**；重装也要跟着退避，不是固定 3 秒。
    [Fact]
    public void Heal_defers_while_a_previous_teardown_is_still_stranded()
    {
        var body = Body(Src("app", "Services", "GestureCoordinator.cs"), "private void HealMouseHookIfDead");
        Assert.Contains("MouseHook.StrandedTeardowns > 0", body);
        Assert.Contains("HookWatch.NextHealGap(_healGapMs, HealMinGapMs, HealMaxGapMs)", body);
        // 判据用的是活的退避值，不是那个常量——常量版本治不了「17 秒重装 5 次」。
        Assert.Contains("HealBeatSkewMs, _healGapMs)", body);
        Assert.DoesNotContain("HealBeatSkewMs, HealMinGapMs)", body);
        // 心跳恢复就复位，否则一次真掉钩要等 30 秒才接回来。
        Assert.Contains("_healGapMs = HealMinGapMs;", body);
        // 推迟那一跳也得记「刚动过手」，否则这行日志每 500ms 刷一遍（部署后实测连打两行）。
        // 范围要收在**推迟那一段里**：真动手那一路本来也写同一个字段，整段身体去 Contains
        // 会因为那一处而通过——第一次拿「删掉这一句」去破坏它，它照样绿，那这条守卫就是假的。
        int defer = body.IndexOf("StrandedTeardowns > 0", System.StringComparison.Ordinal);
        int deferEnd = body.IndexOf("return;", defer, System.StringComparison.Ordinal);
        Assert.True(defer >= 0 && deferEnd > defer, "推迟那一段没找到：这段守卫要跟着它走");
        Assert.Contains("_lastHealAt = Environment.TickCount64;", body.Substring(defer, deferEnd - defer));
        Assert.Contains("mouse heal deferred", body.Substring(defer, deferEnd - defer));
    }

    // 回调里**不许再出现**跨进程查询；剩下的那一句（纯 user32 的 WindowFromPoint）仍要计时。
    //
    // 这一条原来是「两处跨进程查询都得被计时包住」，因为那时它们真的在回调里。事故记录：
    // `hook-proc-name took 3187ms` 与 `hook-callback took 6469ms chain=0ms ours=6469ms`——
    // chain=0 排除了别人的钩子也排除了注入，几秒全在这两句上；低级钩子是全局串行的，
    // 那几秒里**全系统**鼠标跟着卡住，而 Windows 不会为它留下任何一行日志。
    //
    // 现在改成更强的形状：**跨进程查询根本不许进回调**（答案由 Native.HookProbe 在后台线程
    // 算好，回调只做两次读，见该类类头）。所以这里从「包住了吗」变成「不许出现吗」——
    // 计时留着是为了纯 user32 的那一句仍然慢时能自己报名，而跨进程那句改由 DoesNotContain 钉死。
    // 让它变红的改动：把 `Win32.ProcessNameForWindow` / `Win32.ProcessNameForPid` 重新写回
    // CallbackCore（功能完全等价、测试全绿、手感也正常），直到下一次真卡住为止。
    [Fact]
    public void The_callback_makes_no_cross_process_query_and_times_the_one_it_keeps()
    {
        // **范围要盖住整个回调路径，不只是 CallbackCore 本身。**
        // 上一版只查 CallbackCore 的方法体，于是漏掉了下一层的 ReplayClick —— 它同样被回调
        // 同步调用（每次右键抬起都走），里面那句 `Win32.InputNotBlockedNow()` 是同一个病，
        // 修好另外两处之后它成了最后一条。要它变红：把 `_canInject` 改回 `?? Win32.InputNotBlockedNow`。
        var raw = Src("app", "Native", "MouseHook.cs");
        var body = Body(raw, "private IntPtr CallbackCore");
        // 纯 user32 的命中测试还留着：WindowFromPoint 不碰别的进程的对象，留在回调里是安全的。
        AssertWraps(body, "hook-window-at", "Win32.WindowAtPoint(data.X, data.Y)");
        // 要 OpenProcess 的那两句，一句都不许回到回调里。
        Assert.DoesNotContain("Win32.ProcessNameForWindow(", body);
        Assert.DoesNotContain("Win32.ProcessNameForPid(", body);
        // 进程名改走探针的缓存读，而那格仍要有名字：它第一次真的可能慢（冷缓存时排一次解析）。
        AssertWraps(body, "hook-proc-name", "_procNameForPid(");
        // 「能不能接管」也走探针——那一问要 OpenProcess + 读 token，与上面同一个理由。
        Assert.Contains("_mayEngage(", body);
        // **补发前那道闸同样不许自己去打 Win32**：它只准读探针的快照。
        //
        // 盯的是**构造函数里那两行**，不是 `ReplayClick` 的方法体——上一版盯错了地方，
        // 于是把 probe 分支删掉（退回 `?? Win32.InputNotBlockedNow`）它照样绿，而那正是
        // 本次真实的回归。这份守卫已经假绿过一次，记在这里免得再犯。
        var ctor = Body(raw, "public MouseHook(int holdMs");
        Assert.Contains("if (probe != null) _canInject = () => probe.Injectable;", ctor);
        Assert.Contains("else _canInject = canInject ?? Win32.InputNotBlockedNow;", ctor);
    }

    // 探针不得被叫成「每拍问一次」。那句要 OpenProcess，而本机实测它能到秒级（见 HookProbe 类头）；
    // 无条件每 500ms 问一次等于把一个已知会卡的操作放大到每分钟 120 次——比修之前还频繁，
    // 只是搬到了后台线程。搬走的是「卡住输入链」，不是「凭空多一倍的跨进程查询」。
    //
    // 让它变红：把 Refresh 改回无条件 `Request(0)`（功能完全等价、测试全绿），
    // 直到某台机器上它真的把后台线程按死为止。
    [Fact]
    public void The_probe_re_probes_on_window_change_not_on_every_tick()
    {
        var body = Body(Src("app", "Native", "HookProbe.cs"), "public void Refresh()");
        // 必须先比一次前台窗口句柄（纯 user32），没换且快照还新鲜就直接返回。
        Assert.Contains("ForegroundWindow()", body);
        Assert.Contains("_lastForeground", body);
        Assert.Contains("SnapshotMaxAgeMs", body);
        // 而且这个「没换就不问」必须让位给一次真正的刷新：只 Request 不判断，等于没节流。
        Assert.Contains("return;", body);
    }

    // —— 回调归因的两格：链（CallNextHookEx）/ 剩下才是我们自己的计算 ——
    //
    // 拆格之前只有一格 `chain=`，它同时装着「跑完排在我们后面的那些钩子」和「我们自己的
    // SendInput」，于是 `chain=4500ms ours=0ms` 有两种读法相反的读法，而我按第一种说过
    // 「92% 不是我们的」——那句是说早了。拆开的当天（21:07）拿到判决性读数：
    // `callbackMax=313ms chainMax=125ms injectMax=313ms`，慢的是我们自己的注入。
    //
    // 于是补发整体上移到补发线程（见 ReplayOffCallbackTests），回调里的 `inject=` 一格退役。
    // **退役不等于删判据**：接管它的是那条守卫（回调里一个注入出口都不许有）+ 下面这三格
    // 新读数。旧写法（把注入记回链那一格、或者把 `InjectMaxMs` 那套捞回来）由这里的
    // DoesNotContain 拦住。
    [Theory]
    [InlineData(4500, 4500, "chain=4500ms ours=0ms")]
    [InlineData(4500, 0, "chain=0ms ours=4500ms")]
    [InlineData(1890, 1000, "chain=1000ms ours=890ms")]
    [InlineData(1000, 1200, "chain=1200ms ours=0ms")]   // 取整叠过头，ours 兜在 0 上
    public void The_attribution_splits_the_chain_from_our_own_work(long total, long chain, string want)
        => Assert.Equal(want, Clockwork.Native.MouseHook.Attribution(total, chain));

    [Fact]
    public void The_injection_reading_moves_to_the_replay_thread()
    {
        var raw = Src("app", "Native", "MouseHook.cs");
        // 只看代码，不看注释：这几处说明文字里写的全是「从前这里是怎么写的」，
        // 逐行扫过去会被自己的说明判红（OffUiThreadTests.Code 那条注释记着同一个坑）。
        var src = string.Join("\n", raw.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
        // 旧的混合格式与退役掉的那一格都不许回来。
        Assert.DoesNotContain("CrossProcessMs", src);
        Assert.DoesNotContain("_injectMs", src);
        Assert.DoesNotContain("InjectMaxMs", src);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(src, @"_chainMs \+="));
        // 接替的那三格必须**报出去**，否则等于没拆：摘钩前抄账那行与卡顿上下文都要带。
        var coord = Src("app", "Services", "GestureCoordinator.cs");
        Assert.Contains("replayMax={_prevReplayMax}ms replayQueued={_prevReplayQueued}", coord);
        Assert.Contains("replayDropped={_prevReplayDropped}", coord);
        Assert.Contains("replayQueued={replayQueued} replayDropped={replayDropped}", coord);
        Assert.Contains("_prevReplayMax = _mouseHook.ReplayMaxMs;", coord);
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
