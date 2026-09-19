using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clockwork.Core;
using Xunit;

// UI 线程卡顿仪表的守卫。这份测试盯两件事：
//
//   ① **判据**（纯函数）——迟到多少算卡顿、多慢算慢调用。阈值是这条仪表唯一承重的取舍，
//      改错了不会有任何症状，只会让它**该报的时候不报**，而那时我们正需要它。
//   ② **接线**——仪表要在覆盖窗出现的那一刻就打开、在覆盖窗收掉时关掉，且必须与
//      「关幽灵化」挨在一起。这跟 TrailGhostingTests 盯的是同一个顺序，只是多了一格。
public class UiStallWatchTests
{
    // ── 判据 ──

    // 迟到 = 实际间隔 − 名义间隔。这一条减法就是整条仪表的机制：UI 线程被卡住时计时器不走，
    // 解卡后第一拍的实际间隔自己就变长了。算错了（比如忘了减名义间隔）会把每一次正常 Tick
    // 都算成「卡了 250ms」——然后阈值一调就再也不会报，而表面上一切正常。
    [Fact]
    public void Lateness_is_the_overshoot_past_the_nominal_interval()
    {
        Assert.Equal(0, UiStallWatch.LatenessMs(nowMs: 1000, lastTickMs: 750, intervalMs: 250));
        Assert.Equal(4000, UiStallWatch.LatenessMs(nowMs: 5000, lastTickMs: 750, intervalMs: 250));
    }

    // 计时器早到要夹到 0。TickCount64 的粒度约 15.6ms，抖一下就会算出负数——
    // 那不是「提前卡了一下」，当成迟到会让阈值形同虚设。
    [Fact]
    public void An_early_tick_is_not_lateness()
    {
        Assert.Equal(0, UiStallWatch.LatenessMs(nowMs: 740, lastTickMs: 750, intervalMs: 250));
    }

    // 边界取「达到即算」：阈值写的是「超过这么久就算卡顿」，差 1ms 不报会让阈值变成
    // 「阈值+1」，而这类 off-by-one 在日志里表现为「卡了 1499ms 却没记」——永远查不出来。
    [Fact]
    public void The_threshold_is_inclusive()
    {
        Assert.True(UiStallWatch.IsStall(1500, 1500));
        Assert.False(UiStallWatch.IsStall(1499, 1500));
        Assert.True(UiStallWatch.IsSlow(500, 500));
        Assert.False(UiStallWatch.IsSlow(499, 500));
    }

    // 默认阈值必须**明显低于幽灵化的 5 秒**。相等的话，仪表只在系统已经决定盖幽灵窗之后
    // 才开口，那正是它存在的意义被抵消掉的写法。
    [Fact]
    public void The_default_threshold_sits_well_below_the_ghosting_timeout()
    {
        Assert.True(UiStallWatch.DefaultStallMs < 5000,
                    "卡顿阈值不能碰到系统的 HungWindowTimeout（5 秒），否则仪表只在幽灵窗已经出现后才报");
    }

    // ── 探测与上报 ──

    [Fact]
    public void A_normal_tick_reports_nothing_and_a_stalled_one_reports_the_gap()
    {
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId);
        try
        {
            UiStallWatch.Tick(250, 250);          // 准时
            UiStallWatch.Tick(500, 250);          // 准时
            Assert.Empty(lines);

            // 下一拍本该在 750 到，结果 4750 才回来 —— 中间那 4000ms 表一步没走，就是卡了 4000ms。
            UiStallWatch.Tick(4750, 250);
            var one = Assert.Single(lines);
            Assert.Contains("stalled", one);
            Assert.Contains("4000ms", one);
        }
        finally { UiStallWatch.Stop(); }
    }

    // 关着的时候一个字都不能写。日志是唯一的事后线索、又只有 128KB，
    // 没开手势的人（也就没有全屏覆盖窗）不该为这条仪表付任何代价。
    [Fact]
    public void A_disabled_watch_stays_silent()
    {
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId);
        UiStallWatch.Stop();
        UiStallWatch.Tick(999_999, 250);
        UiStallWatch.Note("foreground-nudge", 999_999);
        Assert.Empty(lines);
    }

    [Fact]
    public void A_slow_call_reports_its_own_name_and_the_thread_it_ran_on()
    {
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId);
        try
        {
            UiStallWatch.Note("foreground-nudge", 499);
            Assert.Empty(lines);

            UiStallWatch.Note("foreground-nudge", 500);
            var one = Assert.Single(lines);
            Assert.Contains("foreground-nudge", one);
            Assert.Contains("(thread=ui)", one);
        }
        finally { UiStallWatch.Stop(); }
    }

    // **thread=ui / thread=bg 这一格是承重的。** 同一个名字出现在后台动作线程上（等窗口、
    // 注入锁排队）是无害的；出现在 UI 线程上才是幽灵化的点火源。不区分的话，日志里两者
    // 长得一模一样，而这条仪表存在的全部意义就是把它们分开。
    [Fact]
    public async Task The_same_call_on_a_background_thread_is_marked_as_such()
    {
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId);
        try
        {
            await Task.Run(() => UiStallWatch.Note("foreground-nudge", 900));
            var one = Assert.Single(lines);
            Assert.Contains("(thread=bg)", one);
        }
        finally { UiStallWatch.Stop(); }
    }

    // ── 接线（源码钉子，同 TrailGhostingTests 的口径） ──

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string AppSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "App.xaml.cs"));

    private static string MouseHookSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "Native", "MouseHook.cs"));

    private static string DevChecksSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "DevChecks.cs"));

    private static string ConfigStoreSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "Core", "ConfigStore.cs"));

    private static string ReminderStateStoreSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "Core", "ReminderStateStore.cs"));

    // 提醒状态那条同步补写也要有名字。它是本类唯一一条能卡住 UI 线程的路：两个调用方
    //（OnExit 与 Save(durable:true)）都在 UI 线程上，而后台 RetryLoop 持同一把 WriteLock
    // 做一模一样的 5×100ms 重试写 —— 最坏情形是「等它写完 + 自己再写一遍」。
    [Fact]
    public void The_reminder_flush_is_timed()
    {
        var src = ReminderStateStoreSource();
        int m = src.IndexOf("public static void FlushPending()");
        Assert.True(m >= 0, "找不到 FlushPending：这段守卫要跟着它走");
        var body = src.Substring(m, 1200);
        Assert.Contains("UiStallWatch.Begin()", body);
        Assert.Contains("UiStallWatch.End(\"reminder-flush\", t0)", body);
        // finally 里收尾，理由与 ConfigStore 那处一致：写失败的那一次最需要留下时长。
        Assert.Contains("finally { UiStallWatch.End(", body);
    }

    // **这一条才是承重的**：计时必须把**等 WriteLock** 圈进去。
    //
    // 只包住写本身的话，这一格等于白留 —— 写已经有自己的名字了（config-write:attemptN），
    // 而这条路真正长的那一段恰恰是「排队等后台那把锁」。把 lock 挪到 Begin() 之前不会有任何
    // 症状：日志里 reminder-flush 会一直是毫秒级，看着像「这条路没问题」，而卡顿照旧。
    [Fact]
    public void The_reminder_flush_timer_covers_the_lock_wait()
    {
        var src = ReminderStateStoreSource();
        int m = src.IndexOf("public static void FlushPending()");
        Assert.True(m >= 0, "找不到 FlushPending");
        var body = src.Substring(m, 1200);
        int begin = body.IndexOf("UiStallWatch.Begin()");
        int takeLock = body.IndexOf("lock (WriteLock)");
        Assert.True(begin >= 0, "FlushPending 没有计时");
        Assert.True(takeLock > begin, "计时必须从**拿锁之前**开始，否则等锁那一段仍然没有名字");
    }

    // 仪表必须在**覆盖窗建出来之前**就开。幽灵化是「窗口已经摆在那儿」才会发生的，
    // 开晚了那一笔手势就仍然整条路都没记录——而这两句在代码里只是上下两行，挪一下谁也看不出来。
    [Fact]
    public void The_watch_starts_before_the_trail_window_exists()
    {
        var app = AppSource();
        int start = app.IndexOf("ApplyStallWatch(true)");
        int trail = app.IndexOf("_trail ??= new Views.GestureTrailWindow()");
        Assert.True(start >= 0, "App.xaml.cs 里找不到 ApplyStallWatch(true)：卡顿仪表没接上");
        Assert.True(trail >= 0, "找不到笔迹窗的创建：这段守卫要跟着它走");
        Assert.True(start < trail, "卡顿仪表必须在笔迹窗建出来之前打开");
    }

    // 关窗那条路必须在 null 早退**之前**收表。放在早退之后的话，`_trail` 已经是 null 时
    // 表就留着空转——而 CloseTrail 有多个入口（关手势、装钩失败、退出），漏一格就漏一处。
    [Fact]
    public void Closing_the_trail_stops_the_watch_before_any_early_return()
    {
        var app = AppSource();
        int body = app.IndexOf("private void CloseTrail()");
        Assert.True(body >= 0, "找不到 CloseTrail：这段守卫要跟着它走");
        var tail = app.Substring(body, 600);
        int stop = tail.IndexOf("ApplyStallWatch(false)");
        int early = tail.IndexOf("if (_trail == null) return;");
        Assert.True(stop >= 0, "CloseTrail 里没有 ApplyStallWatch(false)：仪表会漏关");
        Assert.True(early >= 0, "CloseTrail 里找不到那个早退：这段守卫要跟着它走");
        Assert.True(stop < early, "收表要在 null 早退之前，否则 _trail 为 null 那几条路上表会空转");
    }

    // ── 卡顿行的尾巴：2026-09-18 那次复现之后加的 ──
    //
    // 那次日志里只留下 `ui thread stalled 4594ms (threshold 1500ms)`——**时长有了，名字没有**。
    // 而「我们自己的代码等在某处」和「一次阻塞式 GC 停了整个世界」在只有时长时长得一模一样，
    // 修法却完全相反。下面这几条钉的就是「那串能把两者分开的补充事实必须在」。

    // 只加在卡顿行上，且 context 为空时**一个多余空格都不留**：那一行是既定的格式，
    // 只往尾巴上加东西才不会让它变形（上面 Contains "stalled" / "4000ms" 的断言因此仍成立）。
    [Fact]
    public void The_stall_line_carries_the_extra_context_only_when_there_is_one()
    {
        Assert.Equal("ui thread stalled 4000ms (threshold 1500ms)",
                     UiStallWatch.FormatStall(4000, 1500, null));
        Assert.Equal("ui thread stalled 4000ms (threshold 1500ms)",
                     UiStallWatch.FormatStall(4000, 1500, ""));
        Assert.Equal("ui thread stalled 4000ms (threshold 1500ms) gc2=+3",
                     UiStallWatch.FormatStall(4000, 1500, "gc2=+3"));
    }

    [Fact]
    public void The_stall_report_is_wired_to_a_context_provider()
    {
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId,
                           context: () => "gc2=+7");
        try
        {
            UiStallWatch.Tick(9000, 250);
            var one = Assert.Single(lines);
            Assert.Contains("gc2=+7", one);
        }
        finally { UiStallWatch.Stop(); }
    }

    // context 自己抛不能带走那条卡顿行：它要去读 _mouseHook / _trail 那些字段，而那些字段
    // 正好在「刚解卡」这个瞬间可能处于半更新状态。sink 那边有 Report 兜着，context 这一侧没有。
    [Fact]
    public void A_throwing_context_provider_does_not_swallow_the_stall_line()
    {
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId,
                           context: () => throw new InvalidOperationException("boom"));
        try
        {
            UiStallWatch.Tick(9000, 250);
            var one = Assert.Single(lines);
            Assert.Contains("stalled", one);
        }
        finally { UiStallWatch.Stop(); }
    }

    // 关掉之后 context 也不能再被调到——Stop 要把它一起清掉，否则关着仪表还在白跑 GC 计数。
    [Fact]
    public void Stopping_the_watch_also_drops_the_context_provider()
    {
        int calls = 0;
        var lines = new List<string>();
        UiStallWatch.Start(lines.Add, nowMs: 0, uiThreadId: Environment.CurrentManagedThreadId,
                           context: () => { calls++; return "x"; });
        UiStallWatch.Stop();
        UiStallWatch.Tick(9000, 250);
        Assert.Empty(lines);
        Assert.Equal(0, calls);
    }

    // ── 源码钉子：UI 线程上那两条**没有名字**的阻塞路，现在必须各有一个 Probe ──

    // 去掉整行注释再断言。理由与 OffUiThreadTests.Code 一字不差：下面的说明文字里就写着
    // 被禁的那几种写法，不去注释的话 `DoesNotContain` 会被自己的说明判红，
    // 而人第一反应是删掉那条断言——守卫就这么没了。
    private static string Code(string src)
        => string.Join("\n", src.Split('\n').Where(l =>
           {
               var t = l.TrimStart();
               return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
           }));

    private static string AppCode() => Code(AppSource());

    // **自愈那条路**是这次复现的核心：HealMouseHookIfDead 在 UI 线程上摘钩，而
    // MouseHook.Dispose() 内部有两次 Join(1000)——钩子线程正卡在回调里时会双双等满，
    // UI 线程卡满 2 秒。它此前完全没有计时，所以日志里只有一句没有名字的 stalled。
    [Fact]
    public void The_heal_path_times_its_hook_dispose()
    {
        var app = AppCode();
        int heal = app.IndexOf("private void HealMouseHookIfDead");
        Assert.True(heal >= 0, "找不到 HealMouseHookIfDead：这段守卫要跟着它走");
        var body = app.Substring(heal, 2600);
        Assert.Contains("DisposeMouseHook(\"heal\")", body);
        Assert.DoesNotContain("_mouseHook.Dispose()", body);   // 裸摘钩 = 又变回没有名字的阻塞
    }

    // 摘钩那个包装本身必须带计时，且名字要能区分来路——heal / manual / off / reapply
    // 四条路的成因完全不同，混成一个名字等于没记。四个调用点一个都不能漏。
    [Fact]
    public void Disposing_the_hook_is_always_timed_and_named()
    {
        var app = AppCode();
        int helper = app.IndexOf("private void DisposeMouseHook(string why)");
        Assert.True(helper >= 0, "找不到 DisposeMouseHook：摘钩又变回不计时了");
        var body = app.Substring(helper, 700);
        Assert.Contains("UiStallWatch.Begin()", body);
        Assert.Contains("UiStallWatch.End(\"hook-dispose:\" + why, t0)", body);
        Assert.Contains("_mouseHook.Dispose()", body);          // 真正摘钩那一句得在包装里
        foreach (var why in new[] { "heal", "manual", "off", "reapply" })
            Assert.Contains($"DisposeMouseHook(\"{why}\")", app);
    }

    // 看门狗那一跳也要有名字：CursorWatchTick 里跑着摘钩 + 重装，
    // 是 UI 线程上唯一一条能卡满 2 秒的路。
    [Fact]
    public void The_cursor_watch_tick_is_timed()
    {
        var app = AppCode();
        int tick = app.IndexOf("private void CursorWatchTick()");
        Assert.True(tick >= 0, "找不到 CursorWatchTick：这段守卫要跟着它走");
        var body = app.Substring(tick, 800);
        Assert.Contains("CursorWatchTickCore()", body);
        Assert.Contains("UiStallWatch.End(\"cursor-watch\", t0)", body);
    }

    // 笔迹那两跳也必须走带计时的包装，不能在构造 MouseHook 时内联 lambda：
    // 那是 AllowsTransparency 的整屏分层窗，WPF 走软件渲染，每帧重合成 ≈14MB，
    // 而钩子每个采样点都会让它重画一次（高刷鼠标一笔几百个点）。
    [Fact]
    public void The_trail_callbacks_are_timed()
    {
        var app = AppCode();
        Assert.Contains("trailPoint: TrailPoint, trailEnd: TrailEnd", app);
        Assert.DoesNotContain("trailPoint: (x, y, armed)", app);   // 内联 lambda = 绕过计时
        foreach (var name in new[] { "\"trail-point\"", "\"trail-finish\"" })
            Assert.Contains(name, app);
    }

    // 面板构建那一段（问前台进程 → 建页 → 建动作格 → 建整扇窗）也要有名字。
    //
    // 它是 `TogglePanel` 里唯一没被量过的部分（`Popup()` 的前台等待另有 foreground-wait），
    // 而 2026-09-18 那次 2547ms 的卡顿同一秒落下的正是 `panel shown but foreground not acquired`。
    // 关键是**时间窗必须真的罩住那几句**：Begin 挪到建页之后、或者 End 挪到 Popup 之后，
    // 都不会有任何症状——日志里照样有一行 panel-build，只是它量的是别的东西。
    [Fact]
    public void The_panel_build_is_timed()
    {
        var app = AppCode();
        int m = app.IndexOf("public void TogglePanel()");
        Assert.True(m >= 0, "找不到 TogglePanel：这段守卫要跟着它走");
        var body = app.Substring(m, 2600);
        int begin = body.IndexOf("Core.UiStallWatch.Begin()");
        int foreground = body.IndexOf("Win32.ForegroundProcessName()");
        int build = body.IndexOf("new Views.QuickPanelWindow(");
        int end = body.IndexOf("Core.UiStallWatch.End(\"panel-build\", t0)");
        Assert.True(begin >= 0 && end >= 0, "TogglePanel 的建面板段没有计时");
        Assert.True(begin < foreground, "计时必须从**问前台进程之前**开始");
        Assert.True(foreground < build, "前台进程必须在建面板之前问（时序命门，别挪）");
        Assert.True(build < end, "计时必须罩住 new QuickPanelWindow(...)，否则量的是别的东西");
        Assert.Contains("finally { Core.UiStallWatch.End(\"panel-build\", t0); }", body);
    }

    // 卡顿行要真的带上整机事实：前三格正是把「我们的代码」和「一次阻塞式 GC」分开的东西。
    // 少了它们，下一次复现还是只剩一句「卡了多久」。
    [Fact]
    public void The_stall_context_reports_gc_and_hook_liveness()
    {
        var app = AppCode();
        int ctx = app.IndexOf("private string StallContext()");
        Assert.True(ctx >= 0, "找不到 StallContext：卡顿行又只剩时长了");
        var body = app.Substring(ctx, 1200);
        Assert.Contains("GC.CollectionCount(2)", body);
        Assert.Contains("GC.GetTotalAllocatedBytes", body);
        Assert.Contains("SinceBeatMs", body);
        Assert.Contains("context: StallContext", app);   // 写了但没挂上去等于没写
    }

    // 钩子回调本身也要有名字。它是唯一能把「钩子为什么沉默」分成两种病的东西：
    //   · 我们的回调被堵（一次阻塞式 GC 停掉整个世界）→ 留下一行 thread=bg 的 slow；
    //   · 回调压根没被叫到（上游钩子链被堵 / 已被系统摘掉）→ 一行都没有，只有沉默。
    // 两种病此前都只留下「沉默 937ms」，修法却相反（一个查我们自己，一个查排在前面的工具）。
    [Fact]
    public void The_hook_callback_is_timed()
    {
        var hook = Code(MouseHookSource());
        Assert.Contains("Core.UiStallWatch.End(\"hook-callback\", t0)", hook);
        Assert.Contains("return CallbackCore(code, wParam, lParam);", hook);
        Assert.Contains("private IntPtr CallbackCore(int code, IntPtr wParam, IntPtr lParam)", hook);
        // 委托必须仍然指向**包装**那个：指到 Core 上就绕开了计时，而且没人看得出来。
        Assert.Contains("_proc = Callback;", hook);
    }

    // 卡顿行还要带上**上游延迟**：只报 sinceBeat 分不开「链被排在前面的工具堵住」和
    // 「我们自己的钩子被系统摘掉」，而这两种病的修法相反（一个只能抢链首，一个重装就好）。
    // 本机同时跑着 Logi Options+ / PowerToys / 热键助手，三家都装 WH_MOUSE_LL，不是假想。
    [Fact]
    public void The_stall_context_also_reports_upstream_hook_latency()
    {
        var app = AppCode();
        int ctx = app.IndexOf("private string StallContext()");
        Assert.True(ctx >= 0, "找不到 StallContext");
        var body = app.Substring(ctx, 1200);
        Assert.Contains("UpstreamMaxMs", body);
        // 没装钩子时必须报 "-" 而不是 "0ms"：0ms 读起来像「一点没迟到」，
        // 而真相是「没有钩子可问」——把「没有」和「很好」混成一格，正是这条仪表要避免的错。
        Assert.Contains("_mouseHook == null ? \"-\"", body);
    }

    // 「这一段时间里覆盖窗在不在屏幕上」必须**锁存**，不能现读。
    //
    // 这是本文件里少见的「字段会在最关键的那一次说谎」的守卫。卡顿行在解卡之后才落笔，
    // 而一笔手势几百毫秒、一次卡顿 4.6 秒——解卡时窗早已 HideSoon 藏掉，现读 IsVisible
    // 必然得到 hidden。于是**偏偏在「整屏覆盖窗就是元凶」的复现里，这一格报成「与覆盖层无关」**，
    // 而它存在的全部理由就是把用户问的那件事（要不要动覆盖层）答准。
    [Fact]
    public void The_stall_context_reports_the_latched_trail_visibility_not_the_current_one()
    {
        var app = AppCode();
        int ctx = app.IndexOf("private string StallContext()");
        Assert.True(ctx >= 0, "找不到 StallContext");
        var body = app.Substring(ctx, 1200);
        Assert.Contains("_trailUpSinceStall", body);            // 读的是锁存值
        Assert.DoesNotContain("_trail.IsVisible", body);        // 而不是此刻的可见性
        // 语义变了就得换名字：旧名字 trail= 的含义是「此刻」，沿用会让读过旧日志的人以为可比。
        Assert.Contains("trailUp={trail}", body);
        Assert.DoesNotContain("trail={trail}", body);
        // 锁存必须每写一行卡顿就清一次，否则一次手势会让之后每一行都报 up。
        Assert.Contains("_trailUpSinceStall = false;", body);
    }

    // 锁存点必须落在**那句调用之前**：卡顿完全可能就发生在这句调用里面，
    // 而卡顿行是解卡之后才写的——放到调用之后，这一笔开头的卡顿就漏掉了。
    // 收笔那一路（TrailEnd）是「一笔确实把覆盖窗摆上过屏」最便宜的取证点，也得锁。
    [Fact]
    public void The_trail_latch_is_set_before_the_call_and_also_on_finish()
    {
        var app = AppCode();
        int p = app.IndexOf("private void TrailPoint(int x, int y, bool armed)");
        Assert.True(p >= 0, "找不到 TrailPoint");
        var pointBody = app.Substring(p, 900);
        int latch = pointBody.IndexOf("_trailUpSinceStall |= ");
        int call = pointBody.IndexOf("_trail?.Point(");
        Assert.True(latch >= 0, "TrailPoint 没有锁存覆盖窗可见性");
        Assert.True(call > latch, "锁存必须在 _trail?.Point(...) **之前**，否则这一笔开头的卡顿会漏掉");

        int e = app.IndexOf("private void TrailEnd()");
        Assert.True(e >= 0, "找不到 TrailEnd");
        var endBody = app.Substring(e, 600);
        int endLatch = endBody.IndexOf("_trailUpSinceStall |= ");
        int finish = endBody.IndexOf("_trail?.Finish(");
        Assert.True(endLatch >= 0 && finish > endLatch, "TrailEnd 也要在收笔前锁一次");
    }

    // 自愈/手动重钩那一行也要带上上一任钩子的上游延迟峰值。这一行是**事后**唯一能拿到
    // 那任钩子账目的地方——摘掉之后就再也问不到了（与 prevEverFired / prevRightButton 同理）。
    [Fact]
    public void The_heal_line_carries_the_previous_hooks_upstream_peak()
    {
        var app = AppCode();
        Assert.Contains("upstreamMax={_prevUpstream}ms", app);
    }

    // 峰值必须在**两条**记账路上都抄下来（自愈、手动「重新挂钩」）。
    // 只抄一条的话，恰恰在那条路上日志里会留一个恒为 0 的假读数——而 0 看起来完全正常。
    [Fact]
    public void Every_hook_accounting_site_captures_the_upstream_peak()
    {
        var app = AppCode();
        Assert.Equal(2, app.Split("_prevUpstream = _mouseHook.UpstreamMaxMs;").Length - 1);
    }

    // ── 与上游延迟**成对**的那一格：我们自己回调耗时的峰值 ──
    //
    // 为什么必须成对：上游延迟回答「是不是**别人**堵了链」，回调耗时回答「是不是**我们自己**
    // 要被摘了」。Windows 的 LowLevelHooksTimeout（本机实测 300ms）是**回调**的预算，
    // 超了就把钩子静默摘掉；摘掉之后上游延迟永远停在最后那个小值上，看着一切正常。
    // 而 `hook-callback` 那行 slow 日志的阈值是 500ms —— **300~500ms 这一段恰好是
    // 「我们正在把自己搞死」的区间，却一声不响**。这一格没有阈值、一任钩子只有一个数，不会刷屏。
    [Fact]
    public void The_heal_line_carries_the_previous_hooks_callback_peak()
    {
        var app = AppCode();
        Assert.Contains("callbackMax={_prevCallbackMax}ms", app);
    }

    [Fact]
    public void Every_hook_accounting_site_captures_the_callback_peak()
    {
        var app = AppCode();
        // 同上游延迟那一格：只抄一条路的话，恰恰在那条路上会留一个恒为 0 的假读数，而 0 看着完全正常。
        Assert.Equal(2, app.Split("_prevCallbackMax = _mouseHook.CallbackMaxMs;").Length - 1);
    }

    [Fact]
    public void The_stall_context_also_reports_our_own_callback_peak()
    {
        var app = AppCode();
        int ctx = app.IndexOf("private string StallContext()");
        Assert.True(ctx >= 0, "找不到 StallContext");
        var body = app.Substring(ctx, 1400);
        Assert.Contains("CallbackMaxMs", body);
        Assert.Contains("callbackMax={", body);
    }

    // 峰值必须在**回调自己那个 finally 里**记，而且要在交 slow 日志之前。
    // 少了它，300~500ms 那一段就永远不可见——而那正是钩子被摘的区间。
    [Fact]
    public void The_callback_peak_is_recorded_inside_the_hook_callback()
    {
        var hook = Code(MouseHookSource());
        int c = hook.IndexOf("private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)");
        Assert.True(c >= 0, "找不到 Callback：这段守卫要跟着它走");
        var body = hook.Substring(c, 700);
        int write = body.IndexOf("Volatile.Write(ref _callbackMax, ms)");
        int slow = body.IndexOf("UiStallWatch.End(\"hook-callback\"");
        Assert.True(write >= 0, "回调里没有记耗时峰值");
        Assert.True(body.Contains("Environment.TickCount64 - t0"), "峰值必须来自真实的调用耗时");
        Assert.True(slow > write, "先记峰值再交 slow 日志——反过来的话，slow 那行抛了峰值就丢了");
        // 峰值必须真的被读出来用（声明了没人读 = 白留），两处消费点都要在。
        Assert.Contains("public uint CallbackMaxMs => Volatile.Read(ref _callbackMax);", hook);
    }

    [Fact]
    public void The_hook_probe_reports_our_own_callback_peak()
    {
        var dev = Code(DevChecksSource());
        Assert.Contains("real.CallbackMaxMs", dev);
        Assert.Contains("回调耗时峰值=", dev);
    }

    // 上游延迟怎么量的：拿系统给这条事件的时刻戳（MSLLHOOKSTRUCT.time）跟现在比。
    // 三个细节缺一不可——
    //   · uint 减法：TickCount 与 time 都是 32 位、同样 49.7 天回绕，转成 int 相减会在回绕点炸出负数；
    //   · time==0 时不算：系统没填这一格，照算会得到一个假的天文数字；
    //   · 量在注入放行**之后**：自己补发的那批事件是我们刚造出来的，量它永远是 0，白占峰值。
    [Fact]
    public void The_upstream_latency_is_measured_from_the_event_timestamp()
    {
        var hook = Code(MouseHookSource());
        Assert.Contains("public uint UpstreamMaxMs", hook);
        Assert.Contains("unchecked((uint)Environment.TickCount - data.Time)", hook);
        Assert.Contains("data.Time != 0", hook);
        int inject = hook.IndexOf("data.DwExtraInfo == Win32.InjectTag");
        int upstream = hook.IndexOf("unchecked((uint)Environment.TickCount - data.Time)");
        Assert.True(inject >= 0, "注入放行那一句不见了：上游延迟会把自己补发的事件也算进去");
        Assert.True(upstream > inject, "上游延迟必须在注入放行之后才量");
    }

    // 这一格还得能**当场量**：日志要等下一次故障才填得上，而 --hookprobe 是唯一能立刻回答
    // 「这台机器上链到底拖了我们多久」的地方（它跑 12 秒、要真人动鼠标按右键）。
    // 探针里必须真的读那一格；抄一份自己算的近似值就等于没验。
    [Fact]
    public void The_hook_probe_reports_the_upstream_latency()
    {
        var dev = Code(DevChecksSource());
        int probe = dev.IndexOf("private void RunHookProbe()");
        Assert.True(probe >= 0, "找不到 RunHookProbe");
        var body = dev.Substring(probe, 4000);
        Assert.Contains("UpstreamMaxMs", body);
        Assert.Contains("上游延迟峰值", body);
        // 必须在 Dispose 之前抄账：与 App.HealMouseHookIfDead 同一条规矩。
        Assert.True(body.IndexOf("realUpstream = real.UpstreamMaxMs") < body.IndexOf("real.Dispose()"),
                    "上游延迟要在摘钩之前抄下来");
    }

    // 全程序唯一一条「在调用线程上反复重试并 Thread.Sleep」的文件 I/O，而调用线程常常就是 UI 线程。
    // 本机跑着坚果云（带文件系统过滤驱动）与 GoodSync，配置目录又在 D:\Backup 下 ——
    // 那句「瞬时占用（OneDrive/索引/杀软持句柄）」的注释在这里是实景，不是假设。
    [Fact]
    public void The_config_write_retry_loop_is_timed()
    {
        var store = Code(ConfigStoreSource());
        int m = store.IndexOf("private static bool WriteTextAtomic(");
        Assert.True(m >= 0, "找不到 WriteTextAtomic");
        var body = store.Substring(m, 2000);
        Assert.Contains("UiStallWatch.Begin()", body);
        // 名字要带上**第几次才成功**：attempt1 是常态，attempt3 就说明有人持着句柄。
        // 计数写在循环外的话名字永远是 attempt1 —— 那一格就白留了。
        Assert.Contains("UiStallWatch.End($\"config-write:attempt{attempt}\", t0)", body);
        Assert.Contains("attempt = i + 1;", body);
        // 必须在 finally 里收尾：throwOnFail 那条路是**抛出**，写盘失败同样要留下时长与次数。
        Assert.Contains("finally { UiStallWatch.End(", body);
    }

    // 保存配置那条路整体也要有名字：它串起了序列化、写盘、重建热键、重装钩子四件事，
    // 而其中任何一件都可能在 UI 线程上以秒计。拆成壳 + Core 是为了让早退那条路也被计时。
    [Fact]
    public void The_config_save_path_is_timed()
    {
        var app = AppCode();
        int save = app.IndexOf("public void SaveConfig()");
        Assert.True(save >= 0, "找不到 SaveConfig");
        int core = app.IndexOf("private void SaveConfigCore()");
        Assert.True(core > save, "找不到 SaveConfigCore");
        // 只取「壳」那一段：定长窗口会伸进 Core 的正文，于是 DoesNotContain 那条会被
        // Core 里本来就有的 _configSuperseded 判红 —— 看起来像守卫坏了，其实是我切错了。
        var shell = app.Substring(save, core - save);
        Assert.Contains("SaveConfigCore()", shell);
        Assert.Contains("UiStallWatch.End(\"config-save\", t0)", shell);
        // 早退（_configSuperseded）必须留在 Core 里，不能挪到壳里 —— 挪上去就绕开了计时。
        Assert.DoesNotContain("_configSuperseded", shell);
    }
}
