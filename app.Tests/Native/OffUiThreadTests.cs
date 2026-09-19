using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// 「哪些活绝不能在 UI 线程上干」的守卫。
//
// 这条纪律是被两次真实故障逼出来的（用户报的「偶尔手势操作会造成无法点击，关闭桌面窗口管理器才正常」）：
//
//   UI 线程被卡住超过 5 秒（系统的 HungWindowTimeout）→ DWM 在那扇**铺满整块屏**的手势笔迹窗上
//   盖一块**点得中**的幽灵窗 → 整个桌面点不动，用户只能去杀 dwm.exe。
//
// 于是「UI 线程上不许出现可能等很久的跨进程调用」从一句风格建议变成了硬约束。这份测试盯的
// 全是**改回去也不会有任何症状**的地方——编译全绿、单测全绿、只在用户那边偶发——
// 而它们恰恰最容易被顺手改回去（因为「直接在 UI 线程上调」看起来总是更简单）。
//
// 另有 Core.UiStallWatchTests 盯着卡顿仪表本身；这里盯的是**代码形状**。
public class OffUiThreadTests
{
    private static string AppRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return Path.Combine(d!.FullName, "app");
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppRoot() }.Concat(parts).ToArray()));

    // 去掉整行注释再断言。
    //
    // **这一步不是洁癖，是必需的**：这几处注释里都写着「原先这里是怎么写的」——
    // 比如 WindowManager 里那句 ``原先这里写的是 `disp.Invoke(...)` ``。
    // 不去注释的话，`DoesNotContain("disp.Invoke")` 会被自己的说明文字判红，
    // 而人第一反应是删掉那条断言——守卫就这么没了。
    // 本项目所有注释都是 `//` 行首风格（没有块注释），所以按行丢就够，且行为可预测。
    private static string Code(string src)
        => string.Join("\n", src.Split('\n').Where(l =>
           {
               var t = l.TrimStart();
               return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
           }));

    // 全仓 .cs 里所有「调用某方法」的位置 —— 用来找**散落的调用点**。
    private static (string File, int Line, string Text)[] CallSites(string methodName)
    {
        var hits = new List<(string, int, string)>();
        foreach (var f in Directory.EnumerateFiles(AppRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            int n = 0;
            foreach (var raw in File.ReadLines(f))
            {
                n++;
                var t = raw.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;   // 注释不算
                if (!t.Contains(methodName + "(")) continue;
                if (t.Contains("static bool " + methodName) || t.Contains("static extern")) continue;   // 声明不算
                hits.Add((Path.GetFileName(f), n, raw.Trim()));
            }
        }
        return hits.ToArray();
    }

    private static string BodyOf(string src, string signature)
    {
        int start = src.IndexOf(signature);
        Assert.True(start >= 0, $"找不到 {signature}：这段守卫要跟着它走");
        int end = src.IndexOf("\n    }", start);
        Assert.True(end > start, $"{signature} 的方法体没找到收尾括号");
        return Code(src.Substring(start, end - start));
    }

    // **`ForceForeground` 只能经由 ForegroundNudge 调用。**
    // 它内部用 AttachThreadInput 挂输入队列，所以调用线程必须有消息泵；而「找一条有泵的线程」
    // 本身有代价（UI 线程有泵，但那是跨进程同步调用，会把 UI 线程按住甚至永久挂住）。
    // 那代价现在收在 ForegroundNudge 的专属牺牲线程里，并且给调用方加了超时。
    // 一旦有人图省事在某个调用点直接写 `Win32.ForceForeground(h)`，那一条路就绕开了超时，
    // 也绕开了「哪条线程承担」这个决定——而它的表现只是「面板偶尔抢不到焦点」，查起来非常远。
    [Fact]
    public void ForceForeground_is_only_reached_through_the_nudge()
    {
        var callers = CallSites("ForceForeground")
            .Where(h => h.File is not ("Win32.cs" or "ForegroundNudge.cs"))
            .ToArray();
        Assert.True(callers.Length == 0,
            "只能经 ForegroundNudge.Activate 调用 ForceForeground，发现直接调用：" +
            string.Join("; ", callers.Select(c => $"{c.File}:{c.Line} {c.Text}")));
    }

    // ForegroundNudge 必须真的存在那条专属线程，且**带超时**。
    // 少了超时，一次不返回的 SetForegroundWindow 会把调用方永远吊住——那正是要修的病。
    [Fact]
    public void The_nudge_thread_has_a_bounded_wait()
    {
        var src = Code(Source("Native", "ForegroundNudge.cs"));
        Assert.Contains("Name = \"Clockwork.Foreground\"", src);
        Assert.Contains("req.Done.Wait(timeoutMs)", src);
        Assert.Contains("timeoutMs = DefaultTimeoutMs", src);
        // 超时必须低于卡顿阈值，否则这条等待自己就会变成一次「UI 线程卡顿」的告警来源。
        Assert.True(Clockwork.Core.UiStallWatch.DefaultStallMs > 1200,
                    "ForegroundNudge 的 1200ms 超时必须低于 UiStallWatch 的卡顿阈值");
    }

    // **`SetForeground` 不许再把 ForceForeground 切到 UI 线程上执行。**
    // 这正是修掉的那个写法：`disp.Invoke(() => Win32.ForceForeground(...))`。
    // 它看起来比「专用线程 + 队列 + 超时」简单得多，所以最可能被改回来。
    [Fact]
    public void WindowManager_no_longer_marshals_the_nudge_onto_the_ui_thread()
    {
        var body = BodyOf(Source("Native", "WindowManager.cs"), "public static bool SetForeground(string process)");
        Assert.Contains("ForegroundNudge.Activate(hs[0])", body);
        Assert.DoesNotContain("disp.Invoke", body);
        Assert.DoesNotContain("Application.Current", body);
    }

    // **`ApplyMouseHook` 不许在 UI 线程上同步等安装。** 它原先直接 `if (_mouseHook.Install())`，
    // 而 Install 内部是 `_installed.Wait(5000)`——那个 5000 正好等于系统的 HungWindowTimeout，
    // 一旦真等满，UI 线程就被卡在幽灵化阈值上。
    // ApplyMouseHook 之所以必须在 UI 线程上，理由是**建那扇 WPF 笔迹窗**，不是安装钩子。
    [Fact]
    public void ApplyMouseHook_does_not_wait_for_the_install_on_the_ui_thread()
    {
        var src = Source("App.xaml.cs");
        int start = src.IndexOf("private void ApplyMouseHook()");
        Assert.True(start >= 0, "找不到 ApplyMouseHook：这段守卫要跟着它走");
        int end = src.IndexOf("\n    /// <summary>安装结果回到 UI 线程", start);
        Assert.True(end > start, "找不到 ApplyMouseHook 的收尾（OnHookInstalled 的文档注释）：这段守卫要跟着它走");
        var body = Code(src.Substring(start, end - start));
        Assert.DoesNotContain("_mouseHook.Install()", body);
        Assert.Contains("hook.Install()", body);        // 在起出来的那条线程里
        Assert.Contains("_hookInstallToken", body);
    }

    // 端口页这两处「本地实测 3ms / 2 秒」是最容易被改回同步的一类：注释里写着实测数字，
    // 而那个数字**只在本地盘上成立**。扫描里有 Process.GetProcesses()、逐 PID 读 PEB、
    // 以及沿工作目录往上 6 层的文件系统探测（网络盘 / 同步盘上单次探测就能等满超时），
    // 而它挂在 5 秒一拍的定时器上；杀进程那边每个占用者 WaitForExit(2000)，三个就是 6 秒。
    // 两条都能单独越过幽灵化的 5 秒阈值。
    [Fact]
    public void The_ports_page_does_not_scan_or_kill_on_the_ui_thread()
    {
        var src = Source("MainWindow.xaml.cs");
        var scan = BodyOf(src, "private void LoadPorts()");
        Assert.Contains("Task.Run(PortReader.GetEntries)", scan);
        Assert.DoesNotContain("SetItems(PortReader.GetEntries())", scan);   // 那个同步写法

        var kill = BodyOf(src, "private void PortKill_Click(");
        Assert.Contains("Task.Run(() => PortReader.FreePort(item))", kill);
        Assert.DoesNotContain("var err = PortReader.FreePort(row.Item);", kill);
    }

    // 安装结果回来时必须过两道闸：号还是最新那一个，且对象还是本次那个。
    // 少了它，一次「已经在飞」的旧安装报失败时会拿**新**钩子去收尾——Dispose 掉一个装好的钩子、
    // 关掉笔迹窗、还弹一句「装不上」，而用户看到的是手势忽然全失灵。
    [Fact]
    public void A_stale_install_result_is_discarded()
    {
        var body = BodyOf(Source("App.xaml.cs"), "private void OnHookInstalled(");
        Assert.Contains("token != _hookInstallToken", body);
        Assert.Contains("ReferenceEquals(hook, _mouseHook)", body);
    }

    // 安装一旦异步化，「等的时候有人把钩子拆了」就成了必然而不是偶然：
    // Teardown() 里有 `_installed.Dispose()`，而被 Dispose 的 ManualResetEventSlim 再 Wait 会抛
    // ObjectDisposedException。原来的同步写法下 Install 与 Dispose 都在 UI 线程上串行，撞不上。
    [Fact]
    public void The_install_wait_survives_the_event_being_disposed_underneath_it()
    {
        var src = Code(Source("Native", "MouseHook.cs"));
        Assert.Contains("catch (ObjectDisposedException)", src);
        // 两处等待都要走那条助手——漏一处就漏一整档竞态。
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(src, @"WaitInstalled\(5000\)").Count);
        Assert.DoesNotContain("_installed.Wait(5000)", src);
    }

    // **`AppendErrorLog` 里不许再出现文件 I/O。**
    //
    // 它从前是「锁 → 截断 → 追加」，而锁是**所有线程共用**的：后台线程（终结器线程上的
    // UnobservedTaskException、端口扫描线程…）在锁里等磁盘时，UI 线程下一次记日志就得陪着等。
    // 这个形状最坏的情况正好是最需要日志的时候——磁盘/网络盘出问题时——于是「日志把 UI 线程按住
    // 5 秒 → 幽灵化 → 用户杀 dwm.exe」这条链子会在排障现场自己长出来。
    //
    // 现在这一段只做内存操作（去重 + 入队），文件 I/O 全在 Clockwork.ErrorLog 那条线程上。
    // 这个改动**没有任何可见症状**：记日志照样能记、文件照样会长，只是慢一点点落盘——
    // 所以它极容易被「顺手简化回去」。这条守卫就是拦这个。
    [Fact]
    public void The_error_log_is_written_off_the_calling_thread()
    {
        var body = BodyOf(Source("App.xaml.cs"), "internal void AppendErrorLog(string line)");
        Assert.Contains("_logQueue.Enqueue", body);
        // 一个都不许漏：这四样是「在调用方线程上碰磁盘」的全部入口。
        Assert.DoesNotContain("AppendAllText", body);
        Assert.DoesNotContain("ReadAllText", body);
        Assert.DoesNotContain("WriteAllText", body);
        Assert.DoesNotContain("FileInfo", body);
        // 也不许去碰文件锁。**这条同时是「锁序无环」的守卫**：锁序固定为
        // `_logFileLock` → `_logLock`，入队侧只拿后者。哪天有人在这里加一句 _logFileLock，
        // 就出现「入队侧等文件锁」= 又回到了改之前的病。
        Assert.DoesNotContain("_logFileLock", body);
    }

    // 时间戳必须在**事件发生那一刻**取，不能等写盘时再取。
    // 入队之后这两件事之间隔了「唤醒写盘线程」这一段，把 Stamp() 挪到排空侧看起来像是
    // 「集中处理更干净」，实际会把「排队等了 200ms」写成「200ms 后才发生」——
    // 而这份日志的读法就是拿它跟事件查看器对齐时间线。
    [Fact]
    public void Log_lines_are_stamped_when_the_event_happens_not_when_they_reach_disk()
    {
        var enqueue = BodyOf(Source("App.xaml.cs"), "internal void AppendErrorLog(string line)");
        Assert.Contains("Stamp()", enqueue);

        var drain = BodyOf(Source("App.xaml.cs"), "private void DrainQueueLocked(string path)");
        Assert.DoesNotContain("Stamp()", drain);
    }

    // 写盘线程必须是**后台**线程。
    // 前台线程会让「用户点了退出，进程还在等一条没人叫醒的线程」——那正是 OnExit 那个
    // 300ms 超时要避免的另一种形态。名字也一并钉住：它是排障时唯一能认出这条线程的东西。
    [Fact]
    public void The_log_writer_thread_is_a_named_background_thread()
    {
        var body = BodyOf(Source("App.xaml.cs"), "private void EnsureLogWriter()");
        Assert.Contains("IsBackground = true", body);
        Assert.Contains("Name = \"Clockwork.ErrorLog\"", body);
        // 双检：两个线程同时第一次记日志时不能起出两条写盘线程（那就不是单写者了）。
        Assert.Contains("lock (_logStartLock)", body);
    }

    // 正常退出的收尾必须真的补上队列里最后那几行。
    // 写盘线程是 IsBackground，进程一走队列跟着没；而「刚出过故障就退出」正是用户最可能做的动作。
    [Fact]
    public void The_exit_path_flushes_the_log_queue()
    {
        var body = BodyOf(Source("App.xaml.cs"), "protected override void OnExit(ExitEventArgs e)");
        Assert.Contains("FlushErrorLog()", body);
    }

    // **两处同步排空都必须带超时，绝不能用无界 `lock`。**
    // 文件若落在已经断掉的网络盘上，写盘线程会卡在一次系统调用里很久；这时 OnExit 正等着拿
    // _logFileLock——无界等就是「点了退出，程序赖着不走」，比少写几行日志难看得多。
    [Fact]
    public void The_synchronous_log_drain_is_bounded()
    {
        var body = BodyOf(Source("App.xaml.cs"), "private bool TryDrainErrorLog(int timeoutMs)");
        Assert.Contains("Monitor.TryEnter(_logFileLock, timeoutMs)", body);
        Assert.DoesNotContain("lock (_logFileLock)", body);

        // 崩溃路径同理：堆栈是这份文件最值钱的一段，但也不值一个永不退出的崩溃处理器。
        var crash = BodyOf(Source("App.xaml.cs"), "private string LogError(Exception? ex)");
        Assert.Contains("Monitor.TryEnter(_logFileLock, LogCrashTimeoutMs)", crash);
        Assert.DoesNotContain("lock (_logFileLock)", crash);

        // 崩溃时必须**先排空队列再写自己的堆栈**，否则时间线会倒过来（堆栈插在它之前发生的事前面）。
        int drainAt = crash.IndexOf("DrainQueueLocked(path)");
        int stackAt = crash.IndexOf("File.AppendAllText(path, $\"[{Stamp()}] {ex}");
        Assert.True(drainAt >= 0 && stackAt > drainAt,
            "LogError 必须先排空队列、再写异常堆栈，否则日志的时间线是倒的");
    }
}
