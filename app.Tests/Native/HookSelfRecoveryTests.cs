using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// A3（钩子线程自救）与 C（让路）在 MouseHook 这一侧的骨架。
//
// 真装一条 WH_MOUSE_LL 再让它超时，会把测试机的整条输入链拖进去；所以行为测试只驱动
// 不装钩子的回调路径（与 MouseHookThreadTests 同一纪律），线程/泵的形状由源码守卫钉住。
public class HookSelfRecoveryTests
{
    private const int WM_MOUSEMOVE = 0x0200, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "app" }.Concat(parts).ToArray()));

    private static string BodyOf(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到 {signature}");
        int end = src.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{signature} 的方法体没有收尾");
        return src.Substring(start, end - start);
    }

    private static IntPtr Send(MouseHook hook, int msg, int x, int y)
    {
        var cb = typeof(MouseHook).GetMethod("Callback", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static object? Field(MouseHook hook, string name)
        => typeof(MouseHook).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hook);

    private static bool Swallowed(IntPtr verdict) => verdict == new IntPtr(1);

    // 窗口峰值必须真的「读后清零」，而 lifetime 峰值不能被连带清掉：
    // 前者是让路判据的输入，后者是重装日志的历史。
    [Fact]
    public void Congestion_peaks_are_read_and_cleared_without_touching_the_lifetime_peak()
    {
        using var hook = new MouseHook(0, () => { }, a => a(), modifierHeld: () => false);
        typeof(MouseHook).GetField("_callbackWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 412u);
        typeof(MouseHook).GetField("_replayWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 87);
        typeof(MouseHook).GetField("_callbackMax", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 999u);

        var (callback, replay) = hook.TakeCongestionPeaks();
        Assert.Equal(412, callback);
        Assert.Equal(87, replay);
        Assert.Equal((0, 0), hook.TakeCongestionPeaks());
        Assert.Equal(999u, hook.CallbackMaxMs);
    }

    // 自救重置 lifetime 账时，窗口峰值必须留给让路采样：超时的证据正是在这两个格里。
    [Fact]
    public void Generation_reset_keeps_the_window_peaks_for_the_yield_sampler()
    {
        using var hook = new MouseHook(0, () => { }, a => a(), modifierHeld: () => false);
        var t = typeof(MouseHook);
        t.GetField("_callbackWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, 412u);
        t.GetField("_replayWindowMax", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, 87);
        t.GetField("_callbackMax", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, 999u);
        t.GetMethod("ResetGenerationMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hook, null);

        Assert.Equal((412, 87), hook.TakeCongestionPeaks());
        Assert.Equal(0u, hook.CallbackMaxMs);
    }

    // 让路中右键必须整笔放行，而且不能把 gate 打开——否则「让路」只是换了个地方吞键。
    [Fact]
    public void Yielding_passes_right_buttons_through_and_keeps_the_gate_cold()
    {
        var gate = new GestureGate(_ => true, 40);
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: gate,
            mayEngage: _ => true, procNameForPid: _ => "notepad", modifierHeld: () => false);
        hook.SetYielding(true);
        Assert.True(hook.Yielding);

        Assert.False(Swallowed(Send(hook, WM_RBUTTONDOWN, 100, 100)));
        Assert.False(gate.Pending);
        Assert.False(Swallowed(Send(hook, WM_RBUTTONUP, 100, 100)));

        hook.SetYielding(false);
        Assert.True(Swallowed(Send(hook, WM_RBUTTONDOWN, 100, 100)));   // 恢复后照旧接管
        gate.Reset();   // 别让 Dispose 走还键那一条注入路
    }

    // 让路的结算消息投不出去时，**进入**那一档要把标志退回去：
    // 结算消息没到钩子线程 = 那一笔被吞掉的按下永远还不了（让路本身又吞了用户一键），
    // 而退回去之后回调继续按老路接管，协调器下一拍会再问一次
    //（见 GestureCoordinator.UpdateGestureYield）——于是这一拍自动重试。
    //
    // 让它变红的改动：把 PostThreadMessage 的返回值丢掉
    //（RequestSelfRecovery 那边是看返回值的——同一份代码里两种写法，不是政策问题）。
    [Fact]
    public void A_failed_yield_post_reverts_the_flag()
    {
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null, modifierHeld: () => false);
        // **一个不可能存在的线程 id**：投递必然失败（PostThreadMessage 答 false）。
        //
        // 不用「起一条线程、记下它的 id、再让它结束」那种写法：Windows 并不保证
        // 立刻不复用那个 id（实测 3000 轮都没碰上，但不是保证）——「大概率不撞上」
        // 不等于「构造上不可能」。这里用一个越界的 id，失败是构造出来的。
        typeof(MouseHook).GetField("_threadId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, uint.MaxValue);
        hook.SetYielding(true);
        Assert.False(hook.Yielding);
    }

    // **退出**那一档相反：标志不能退回去。
    //
    // 这一条**不是红证明**（改之前它也过）：它守的是「别把修复做成另一种坏」——
    // 退出这一边根本没有结算要做（ApplyYield 对 on=false 直接返回），
    // **标志本身就是退出的全部**。退回去 = 让路永远退不掉，
    // 钩子会一直放行右键 = 手势静默失效（而托盘那边看不出来）。
    [Fact]
    public void A_failed_yield_exit_post_keeps_the_flag_off()
    {
        using var hook = new MouseHook(0, () => { }, a => a(), gesture: null, modifierHeld: () => false);
        hook.SetYielding(true);          // 没装钩子 → 只落标志，不投消息
        Assert.True(hook.Yielding);
        typeof(MouseHook).GetField("_threadId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(hook, 4242u);
        hook.SetYielding(false);         // 投递必然失败
        Assert.False(hook.Yielding);
    }

    // 协调器这一层结束让路时，钩子那一层也要归零：两边的「在不在让路」对不上时，
    // 钩子会一直放行右键 = 手势静默失效，而托盘那边看不出来。
    // 让它变红的改动：把 ClearGestureYield 里那句 SetYielding(false) 删掉。
    [Fact]
    public void Clearing_the_yield_clears_the_hook_side_too()
    {
        var body = BodyOf(Source("Services", "GestureCoordinator.cs"), "private void ClearGestureYield");
        Assert.Contains("_mouseHook?.SetYielding(false)", body);
    }

    // 笔画中途的修正只能读缓存；若每个采样点都排一次解析，HookProbe 的 64 格队列会被同一 pid 灌满。
    [Fact]
    public void Mid_stroke_name_correction_reads_the_cache_without_enqueuing()
    {
        var probe = Source("Native", "HookProbe.cs");
        int at = probe.IndexOf("public string CachedProcessName(", StringComparison.Ordinal);
        Assert.True(at >= 0, "CachedProcessName 不见了");
        int end = probe.IndexOf(';', at);
        Assert.True(end > at, "CachedProcessName 的方法体没找到收尾");
        Assert.DoesNotContain("Request(", probe.Substring(at, end - at));
        var hook = Source("Native", "MouseHook.cs");
        Assert.Contains("_cachedProcNameForPid(_gesturePid)", hook);
    }

    // 自救只由泵线程执行；回调里投的是一条消息，不是 SetWindowsHookEx。
    [Fact]
    public void Self_recovery_is_a_pump_thread_action_not_callback_work()
    {
        var src = Source("Native", "MouseHook.cs");
        var pump = BodyOf(src, "private void HookThreadRun()");
        Assert.Contains("WM_HOOK_RECOVER", pump);
        Assert.Contains("RecoverHook()", pump);
        Assert.Contains("WM_YIELD", pump);

        var cb = BodyOf(src, "private IntPtr Callback(");
        Assert.Contains("HookWatch.ShouldSelfRecover", cb);
        Assert.Contains("RequestSelfRecovery()", cb);
        Assert.DoesNotContain("SetWindowsHookEx", cb);
        Assert.DoesNotContain("UnhookWindowsHookEx", cb);

        var recover = BodyOf(src, "private void RecoverHook()");
        Assert.Contains("Volatile.Read(ref _disposePosted)", recover);
        Assert.Contains("Volatile.Write(ref _hook", recover);
        Assert.Contains("SetWindowsHookEx", recover);
        Assert.Contains("_selfRecovered?.Invoke(this", recover);
    }

    // 自救结果回到 UI 时必须对号：旧钩子的迟到报告不许改新钩子的状态。
    [Fact]
    public void A_late_self_recovery_report_is_ignored_by_the_coordinator()
    {
        var coord = Source("Services", "GestureCoordinator.cs");
        Assert.Contains("TakeCongestionPeaks", coord);
        Assert.Contains("ReferenceEquals(source, _mouseHook)", coord);
        Assert.Contains("gesture yield: entered", coord);
        Assert.Contains("ClearGestureYield", coord);
        var tray = Source("TrayIcon.cs");
        Assert.Contains("Gesture_YieldResume", tray);
        Assert.Contains("GestureYieldActive", tray);
        var mgr = Source("Views", "GestureManagerWindow.xaml.cs");
        Assert.Contains("GestureWatchState.Yielding", mgr);
        Assert.Contains("Gesture_WatchYielding", mgr);
    }
}
