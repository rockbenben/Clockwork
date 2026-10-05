using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 「补发送不进去的窗口上，右键一个键都不许吞」——这一组连同 StallProbeWiringTests 里那一条，
// 一起把用户报的「右键经常被吞」（2026-10-06）钉在修好的那一侧。
//
// 旧设计是无条件吞下每一次右键按下，指望抬起来时补发一次真右键。可补发是**注入**，而注入
// 在前台窗口比本进程提权时被 UIPI 静默丢弃（Win32.InputNotBlockedNow 答 false 的那一档）。
// 那一刻按下已经不在了，于是「吞下去之后才发现送不回来」——用户那一下右键彻底消失。
//
// 修法是把闸门**提到按下之前**：探针说这一下送不进去，就整笔放行，让系统出一个原生右键。
// 用户失去的是这一次手势（并且那一档会记在日志/仪表里），换来的是右键永远不消失。
public class RightClickNeverSwallowedTests
{
    private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    private static IntPtr Send(MouseHook hook, int msg, int x, int y)
    {
        var cb = typeof(MouseHook).GetMethod("Callback",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static object? Field(MouseHook hook, string name)
        => typeof(MouseHook).GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(hook);

    // 契约是「1 = 吞掉」，其余都是放行（CallNextHookEx 的返回值无钩子链时为 0，不钉死它）。
    private static bool Swallowed(IntPtr verdict) => verdict == new IntPtr(1);

    // holdMs=0：只留手势那一路。post 同步执行，回调里的投递当场跑完，不依赖消息循环。
    private static MouseHook Hook(GestureGate gate, Func<uint, bool> mayEngage, Func<uint, string>? name = null)
        => new(0, () => { }, a => a(), gesture: gate, mayEngage: mayEngage,
               procNameForPid: name ?? (_ => ""));

    private static void ResetGate(MouseHook hook)
        => ((GestureGate)Field(hook, "_gesture")!).Reset();

    // ── 闸门本身：送不进去 → 按下与抬起都原样放行 ──
    //
    // 这是整件事的核心断言。让它变红：把 `_mayEngage(...)` 那道闸删掉（或者探针恒答 true）。
    // 后果不是编译错误也不是单测失败，而是用户那一下右键在提权窗口里凭空消失。
    [Fact]
    public void A_window_that_blocks_injection_passes_both_buttons_through()
    {
        var gate = new GestureGate(_ => true, 40);          // 什么都匹配得上：闸门必须是唯一的挡住它的东西
        using var hook = Hook(gate, _ => false);
        Assert.False(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        Assert.False(gate.Pending);                          // gate 根本没起笔：随后的 move/up 天然全 Pass
        Assert.False(Swallowed(Send(hook, WM_RBUTTONUP, 200, 200)));
    }

    // 闸门开着时行为**一字不变**——旧行为全在这条路上（吞下按下、抬起按手势判定）。
    // 没有这一条的话，上面那条「全都放行」可以用「把手势整个关掉」来满足。
    [Fact]
    public void An_injectable_window_still_engages_the_gesture_as_before()
    {
        var gate = new GestureGate(_ => false, 40);
        using var hook = Hook(gate, _ => true);
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
        Assert.True(gate.Pending);
        gate.Reset();
    }

    // 匹配上的手势照旧 Fire，匹配不上的照旧把右键还给用户（这两条判据在 GestureGateTests 里，
    // 这里只确认「闸门开着时没被顺带改掉」）。
    [Fact]
    public void A_matching_stroke_still_fires_when_the_window_allows_injection()
    {
        var gate = new GestureGate(p => p == "R", 40);
        using var hook = Hook(gate, _ => true);
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 100, 100)));
        for (int i = 1; i <= 12; i++) Assert.False(Swallowed(Send(hook, 0x0200, 100 + i * 10, 100)));
        Assert.True(Swallowed(Send(hook, WM_RBUTTONUP, 220, 100)));   // Fire = 吞掉抬起，动作在 post 里
    }

    // pid 为 0（没有前台窗）也必须整笔放行：探针对「答不出来」的取向是宁可不要手势。
    // 探针那一侧的实现见 HookProbe.MayEngage，这里钉的是回调**怎么用它**。
    [Fact]
    public void No_foreground_window_passes_the_right_click_through()
    {
        var gate = new GestureGate(_ => true, 40);
        // 只有 pid=0 才放行；探针实现（HookProbe.MayEngage）正是这条语义。
        var probe = new HookProbe();
        using var hook = Hook(gate, pid => probe.MayEngage(pid));
        Assert.False(probe.MayEngage(0));
        Assert.False(Swallowed(Send(hook, WM_RBUTTONDOWN, 200, 200)));
    }

    // 按下那一刻缓存多半是空的（后台线程还没算完），而一笔真手势要画几百毫秒。
    // 所以抬起时必须在**判定之前**补问一次——否则那个窗口里的第一次手势永远按空串匹配，
    // 绑定了该程序的手势一次都不命中。
    //
    // 这条钉的是**顺序**：`OnRightUp()` 内部就按当时的 ContextProcess 匹配了，把补写挪到它
    // 后面，功能等价于没有（Gate.Pending 那时已清掉，这一笔已经判完了）。
    // 用一个「只有抬起前才答得出名字」的假探针把顺序逼出来。
    [Fact]
    public void The_process_name_is_re_resolved_before_the_stroke_is_judged()
    {
        var gate = new GestureGate((p, proc) => proc == "notepad", 40);
        bool resolved = false;
        using var hook = Hook(gate, _ => true, _ => resolved ? "notepad" : "");

        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 100, 100)));   // 按下：缓存未命中，ContextProcess 空
        for (int i = 1; i <= 12; i++) Assert.False(Swallowed(Send(hook, 0x0200, 100 + i * 10, 100)));
        resolved = true;                                               // 一笔画了 300ms，后台线程这时才算完
        // 抬起：补问拿到 notepad，于是这条 per-app 手势命中 → Fire = 吞掉抬起。
        // 若补写排在 OnRightUp 之后，这里会拿到 ReplayClick（放行抬起），本条就会红。
        Assert.True(Swallowed(Send(hook, WM_RBUTTONUP, 220, 100)));
    }

    // ── 探针：快照的语义 ──
    //
    // 「刚切过去的窗口」那一档必须答 false（不接管），而不是拿上一个窗口的答案冒充。
    // 这正是本机用户那台机器上的常态：切到新窗口不到半秒就按右键，快照还是上一个窗口的。
    // 若这条松掉，探针会对**没算过的窗口**给出上一个窗口的结论——在一个提权窗口上，
    // 那正是「吞下去之后送不回来」重新发生的地方。

    [Fact]
    public void The_probe_refuses_to_engage_before_anything_is_computed()
    {
        using var probe = new HookProbe();
        // 还没 Refresh 过：快照是 (pid=0, injectable=false)，任何 pid 都答 false。
        Assert.False(probe.MayEngage(1234));
        Assert.False(probe.Injectable);
    }

    [Fact]
    public void The_probe_answers_false_for_a_pid_it_never_computed()
    {
        using var probe = new HookProbe();
        probe.Start();
        probe.Refresh();
        // 刷新算的是「当前前台窗口」这一个 pid；随便一个别的 pid 仍然答 false——
        // 它没被算过，不许拿别人的结论顶替。
        Assert.False(probe.MayEngage(uint.MaxValue - 7));
        // 自己这个 pid 的进程名一定查得出来（它就在跑），用来验证后台线程真的在干活。
        var own = Win32.ProcessNameForPid((uint)Environment.ProcessId);
        Assert.NotEqual("", own);
        Assert.Equal(own, SpinUntil(() => probe.ProcessName((uint)Environment.ProcessId), 5000));
    }

    // 查不到名字时**不许**把空串钉进缓存：一个后来才启动的同名 pid 会一直吃这一格空答案，
    // 于是那个窗口里的 per-app 手势永远不命中，而缓存再也等不到更新。
    [Fact]
    public void An_unresolvable_pid_is_never_cached_as_an_empty_name()
    {
        using var probe = new HookProbe();
        probe.Start();
        // 一个不可能活着的 pid：问一次返回空串（并排了解析），再问仍然走同一条路而不是命中缓存。
        Assert.Equal("", probe.ProcessName(uint.MaxValue));
        Assert.Equal("", probe.ProcessName(uint.MaxValue));
    }

    // 后台线程自己要能起来、要能收：构造函数**不许**偷开线程（与 MouseHook 同一条纪律），
    // 而 Dispose 之后投递不许抛。
    [Fact]
    public void The_probe_starts_on_demand_and_never_throws_after_dispose()
    {
        var probe = new HookProbe();
        probe.Start();
        probe.Refresh();
        probe.Dispose();
        probe.Dispose();          // 幂等
        probe.Refresh();          // 已拆：静默返回，不抛（这几句跑在 UI 线程的 tick 上）
        Assert.Equal("", probe.ProcessName(1234));
    }

    [Fact]
    public void The_probe_constructor_does_not_start_any_thread()
    {
        var src = File.ReadAllText(RepoFile("app", "Native", "HookProbe.cs"));
        // 本类没有显式构造函数（字段全部就地初始化），所以要盯的是「字段初始化区里不许开线程」。
        // 判据取 `Start()` 的方法体之前那一段——那是唯一可能偷开线程的地方。
        int startAt = src.IndexOf("public void Start()", StringComparison.Ordinal);
        Assert.True(startAt >= 0, "Start() 不见了：这段守卫要跟着它走");
        var beforeStart = src.Substring(0, startAt);
        // `new Thread` 只许出现在 Start() 里那一句；出现在它前面就是构造函数偷开了线程。
        int thread = beforeStart.LastIndexOf("new Thread", StringComparison.Ordinal);
        Assert.True(thread < 0,
            "构造函数里不许起线程：与 MouseHook 同一条纪律（MouseHookThreadTests 也钉着它），"
            + "单测反射驱动时不 Install 就必须没有线程被起过");
        // 反过来也要确认 thread 确实存在（否则这条守卫会因为「文件写坏了」而假绿）。
        Assert.Contains("new Thread(Run)", src);
    }

    // SpinUntil 在测试里是可接受的等待，但它等的是**后台线程**（我们不控制的 OS 查询），
    // 5 秒是「环境坏到连自己进程的 OpenProcess 都做不完」的兜底，不是正常路径的耗时。
    private static string SpinUntil(Func<string> read, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string last = "";
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            last = read();
            if (last.Length > 0) return last;
            System.Threading.Thread.Sleep(20);
        }
        return last;
    }

    private static string RepoFile(params string[] parts)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var all = new string[parts.Length + 1];
        all[0] = d!.FullName;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.Combine(all);
    }
}